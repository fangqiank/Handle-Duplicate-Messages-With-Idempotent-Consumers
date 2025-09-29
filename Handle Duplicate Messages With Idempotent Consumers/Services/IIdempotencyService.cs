using Handle_Duplicate_Messages_With_Idempotent_Consumers.Data;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;
using Microsoft.EntityFrameworkCore;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Services
{
    public class IIdempotencyService(
        AppDbContext context,
        ILogger<IIdempotencyService> logger
        ) : IIdempotencyServiceInterface
    {
        private const int MaxProcessingAttempts = 3;
        private readonly Dictionary<string, int> _processingAttempts = new();
        private readonly HashSet<string> _processingMessages = new();

        public async Task<bool> CheckIfMessageAlreadyProcessedAsync(
            string messageId,
            string consumerName)
        {
            // Only check the main idempotency records - dead-letter queue is for failed processing, not duplicates
            var record = await context.IdempotencyRecords
                .FirstOrDefaultAsync(x => x.MessageId == messageId && x.ConsumerName == consumerName);

            // If record exists, it means the message is being processed or has been processed
            // Either way, it's a duplicate
            return record != null;
        }

        public async Task<ProcessResult> ProcessWithDeadLetterQueueAsync(
            string messageId,
            string consumerName,
            Func<Task<ProcessResult>> processFunc,
            OrderMessage? orderMessage = null)
        {
            // First, check if message is already in dead-letter queue (prevents processing failed messages repeatedly)
            var deadLetterRecord = await context.DeadLetterMessages
                .FirstOrDefaultAsync(x => x.OriginalMessageId == messageId && x.Status == DeadLetterStatus.Pending);

            if (deadLetterRecord != null)
            {
                logger.LogWarning("Message {MessageId} is already in dead-letter queue, skipping processing", messageId);
                return ProcessResult.Fail($"Message {messageId} is in dead-letter queue and requires manual intervention");
            }

            // Check if already processed (standard idempotency check)
            var isAlreadyProcessed = await CheckIfMessageAlreadyProcessedAsync(messageId, consumerName);
            logger.LogWarning("CHECKING IF ALREADY PROCESSED: {MessageId} = {IsProcessed}", messageId, isAlreadyProcessed);

            if (isAlreadyProcessed)
            {
                logger.LogWarning("DUPLICATE DETECTED IN STANDARD CHECK: {MessageId}", messageId);
                // Mark this as a duplicate in the database for statistics
                await MarkDuplicateDetectedAsync(messageId, consumerName, "api");

                // Log duplicate detection
                logger.LogInformation("Duplicate message detected: {MessageId}", messageId);

                var existingResult = await GetProcessingResultAsync(messageId, consumerName);
                return ProcessResult.Ok($"Message already processed: {existingResult}", ExtractOrderIdFromResult(existingResult));
            }

            // Check if message is currently being processed (prevent rapid duplicate submissions)
            var processingKey = $"{messageId}-{consumerName}";
            if (_processingMessages.Contains(processingKey))
            {
                logger.LogWarning("Message {MessageId} is currently being processed, rejecting duplicate submission", messageId);
                return ProcessResult.Fail("Message is currently being processed, please wait");
            }

            // Also check if we have a record that might not be committed yet
            var existingRecord = await context.IdempotencyRecords
                .FirstOrDefaultAsync(x => x.MessageId == messageId && x.ConsumerName == consumerName);

            if (existingRecord != null)
            {
                // Mark this as a duplicate in the database for statistics
                await MarkDuplicateDetectedAsync(messageId, consumerName, "api-db-check");

                // Log duplicate detection
                logger.LogInformation("Duplicate message detected (database check): {MessageId}", messageId);
                return ProcessResult.Ok($"Message already processed: {existingRecord.Result}", ExtractOrderIdFromResult(existingRecord.Result));
            }

            // Track processing attempts
            var attemptKey = $"{messageId}-{consumerName}";
            _processingAttempts.TryGetValue(attemptKey, out var currentAttempt);
            currentAttempt++;

            if (currentAttempt > MaxProcessingAttempts)
            {
                logger.LogWarning("Message {MessageId} exceeded max processing attempts, sending to dead-letter queue", messageId);
                await SendToDeadLetterQueueAsync(messageId, consumerName, currentAttempt, "Max processing attempts exceeded", orderMessage);
                return ProcessResult.Fail("Message exceeded max processing attempts");
            }

            _processingAttempts[attemptKey] = currentAttempt;
            _processingMessages.Add(processingKey); // Mark as being processed

            try
            {
                logger.LogInformation("Processing message {MessageId} (attempt {AttemptNumber})", messageId, currentAttempt);

                // Create a placeholder record to indicate processing is starting
                var recordCreated = await CreateProcessingRecordAsync(messageId, consumerName);

                // If record creation failed (likely due to duplicate), return early
                if (!recordCreated)
                {
                    var existingResult = await GetProcessingResultAsync(messageId, consumerName);
                    return ProcessResult.Ok($"Message already processed: {existingResult}", ExtractOrderIdFromResult(existingResult));
                }

                var result = await processFunc();

                if (result.Success)
                {
                    try
                    {
                        await MarkMessageAsProcessedAsync(messageId, consumerName, $"OrderId: {result.OrderId}");
                        _processingAttempts.Remove(attemptKey); // Clear attempt counter on success
                        logger.LogInformation("Successfully processed message {MessageId} on attempt {AttemptNumber}", messageId, currentAttempt);
                    }
                    catch (Exception dbEx)
                    {
                        logger.LogWarning(dbEx, "Database error while marking message {MessageId} as processed, likely a duplicate", messageId);
                        // If we get here, the processing was successful but we couldn't mark it
                        // This is still a success from the user's perspective
                        _processingAttempts.Remove(attemptKey);
                        return result;
                    }
                }
                else
                {
                    logger.LogError("Message {MessageId} processing failed on attempt {AttemptNumber}: {Error}", messageId, currentAttempt, result.Error);

                    if (currentAttempt >= MaxProcessingAttempts)
                    {
                        await SendToDeadLetterQueueAsync(messageId, consumerName, currentAttempt, result.Error, orderMessage);
                        _processingAttempts.Remove(attemptKey);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing message {MessageId} on attempt {AttemptNumber}", messageId, currentAttempt);

                if (currentAttempt >= MaxProcessingAttempts)
                {
                    await SendToDeadLetterQueueAsync(messageId, consumerName, currentAttempt, ex.Message, orderMessage);
                    _processingAttempts.Remove(attemptKey);
                    return ProcessResult.Fail($"Message processing failed after {currentAttempt} attempts: {ex.Message}");
                }

                // For exceptions, we'll let the caller handle the retry
                throw;
            }
            finally
            {
                // Always remove from processing set, regardless of outcome
                _processingMessages.Remove(processingKey);
            }
        }

        public async Task MarkMessageAsProcessedAsync(
            string messageId,
            string consumerName,
            string result)
        {
            try
            {
                // Try to create or update the record
                try
                {
                    // Check if record already exists (race condition protection)
                    var existingRecord = await context.IdempotencyRecords
                        .FirstOrDefaultAsync(x => x.MessageId == messageId && x.ConsumerName == consumerName);

                    if (existingRecord != null)
                    {
                        // Record already exists, update it instead of creating new one
                        existingRecord.IsProcessed = true;
                        existingRecord.Result = result;
                        existingRecord.ProcessedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        // Create new record
                        var record = new IdempotencyRecord
                        {
                            MessageId = messageId,
                            ConsumerName = consumerName,
                            IsProcessed = true,
                            Result = result,
                            ProcessedAt = DateTime.UtcNow
                        };

                        context.IdempotencyRecords.Add(record);
                    }

                    await context.SaveChangesAsync();
                    logger.LogInformation("Marked message {MessageId} as processed by {ConsumerName}", messageId, consumerName);
                }
                catch (Exception ex)
                {
                    // Check if this is a duplicate key error
                    if (ex is ArgumentException && ex.Message.Contains("same key has already been added"))
                    {
                        // If it's a duplicate key error, this is fine - it means another thread already created the record
                        // Just update the existing record
                        await UpdateExistingRecordAsync(messageId, consumerName, result);
                        logger.LogInformation("Handled concurrent insert for message {MessageId}", messageId);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error marking message {MessageId} as processed", messageId);
                throw;
            }
        }

        private async Task UpdateExistingRecordAsync(string messageId, string consumerName, string result)
        {
            try
            {
                // Find and update the existing record
                var existingRecord = await context.IdempotencyRecords
                    .FirstOrDefaultAsync(x => x.MessageId == messageId && x.ConsumerName == consumerName);

                if (existingRecord != null)
                {
                    existingRecord.IsProcessed = true;
                    existingRecord.Result = result;
                    existingRecord.ProcessedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating existing record for message {MessageId}", messageId);
                // Don't throw here - the main processing already succeeded
            }
        }

        private async Task<bool> CreateProcessingRecordAsync(string messageId, string consumerName)
        {
            try
            {
                // Check if record already exists first
                var existingRecord = await context.IdempotencyRecords
                    .FirstOrDefaultAsync(x => x.MessageId == messageId && x.ConsumerName == consumerName);

                if (existingRecord == null)
                {
                    // Create a placeholder record to indicate processing is starting
                    var record = new IdempotencyRecord
                    {
                        MessageId = messageId,
                        ConsumerName = consumerName,
                        IsProcessed = false, // Not processed yet
                        Result = "Processing...",
                        ProcessedAt = DateTime.UtcNow
                    };

                    context.IdempotencyRecords.Add(record);
                    await context.SaveChangesAsync();
                    return true; // Successfully created new record
                }
                else
                {
                    // Record already exists - this could be a duplicate or concurrent processing
                    // Check if it's already processed
                    if (existingRecord.IsProcessed)
                    {
                        // This is definitely a duplicate - mark it
                        await MarkDuplicateDetectedAsync(messageId, consumerName, "concurrent-detection");
                        return false;
                    }
                    else
                    {
                        // Record exists but is still processing - this is also a form of duplicate
                        await MarkDuplicateDetectedAsync(messageId, consumerName, "concurrent-processing");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create processing record for message {MessageId}", messageId);
                return false;
            }
        }

        public async Task MarkDuplicateDetectedAsync(string messageId, string consumerName, string source = "api")
        {
            try
            {
                logger.LogWarning("ATTEMPTING TO RECORD DUPLICATE: {MessageId} from {Source}", messageId, source);

                // Create a separate record for each duplicate attempt
                var duplicateAttempt = new DuplicateAttempt
                {
                    Id = Guid.NewGuid(),
                    MessageId = messageId,
                    ConsumerName = consumerName,
                    DetectedAt = DateTime.UtcNow,
                    Source = source
                };

                context.DuplicateAttempts.Add(duplicateAttempt);
                var changesSaved = await context.SaveChangesAsync();

                logger.LogWarning("DUPLICATE RECORDED: {MessageId} from {Source}. Changes saved: {ChangesSaved}", messageId, source, changesSaved);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to record duplicate attempt for message {MessageId}", messageId);
                // Don't throw - this is just for statistics
            }
        }

        public async Task<string?> GetProcessingResultAsync(
            string messageId,
            string consumerName
            )
        {
            var record = await context.IdempotencyRecords
                .FirstOrDefaultAsync(x => x.MessageId == messageId && x.ConsumerName == consumerName);

            return record?.Result;
        }

        private async Task SendToDeadLetterQueueAsync(
            string messageId,
            string consumerName,
            int attemptNumber,
            string failureReason,
            OrderMessage? orderMessage = null)
        {
            try
            {
                var deadLetterRecord = new DeadLetterMessage
                {
                    Id = Guid.NewGuid(),
                    OriginalMessageId = messageId,
                    ConsumerName = consumerName,
                    CustomerName = orderMessage?.CustomerName ?? "Unknown",
                    Amount = orderMessage?.Amount ?? 0,
                    OriginalTimestamp = orderMessage?.Timestamp ?? DateTime.UtcNow,
                    AttemptNumber = attemptNumber,
                    FailureReason = failureReason,
                    FailureTimestamp = DateTime.UtcNow,
                    Status = DeadLetterStatus.Pending
                };

                context.DeadLetterMessages.Add(deadLetterRecord);
                await context.SaveChangesAsync();

                logger.LogWarning("Message {MessageId} sent to dead-letter queue with record ID {RecordId}",
                    messageId, deadLetterRecord.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sending message {MessageId} to dead-letter queue", messageId);
            }
        }

        private Guid? ExtractOrderIdFromResult(string? result)
        {
            if (string.IsNullOrEmpty(result))
                return null;

            if (result.StartsWith("OrderId: "))
            {
                var guidString = result.Substring("OrderId: ".Length);
                if (Guid.TryParse(guidString, out var orderId))
                {
                    return orderId;
                }
            }
            return null;
        }

        // Dead-letter queue management methods
        public async Task<List<DeadLetterMessage>> GetDeadLetterMessagesAsync()
        {
            return await context.DeadLetterMessages
                .Where(m => m.Status == DeadLetterStatus.Pending)
                .OrderBy(m => m.FailureTimestamp)
                .ToListAsync();
        }

        public async Task<bool> RetryDeadLetterMessageAsync(Guid deadLetterMessageId)
        {
            try
            {
                var deadLetterMessage = await context.DeadLetterMessages
                    .FirstOrDefaultAsync(m => m.Id == deadLetterMessageId);

                if (deadLetterMessage == null)
                {
                    return false;
                }

                // Reset attempt counter for retry
                var attemptKey = $"{deadLetterMessage.OriginalMessageId}-{deadLetterMessage.ConsumerName}";
                _processingAttempts.Remove(attemptKey);

                deadLetterMessage.Status = DeadLetterStatus.Resolved;
                deadLetterMessage.ResolvedTimestamp = DateTime.UtcNow;
                deadLetterMessage.ResolutionNotes = "Ready for retry";

                await context.SaveChangesAsync();

                logger.LogInformation("Dead-letter message {DeadLetterMessageId} marked for retry", deadLetterMessageId);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrying dead-letter message {DeadLetterMessageId}", deadLetterMessageId);
                return false;
            }
        }

        public async Task<DeadLetterQueueStats> GetDeadLetterQueueStatsAsync()
        {
            var messages = await context.DeadLetterMessages.ToListAsync();

            var pendingMessages = messages.Where(m => m.Status == DeadLetterStatus.Pending).ToList();
            var processedMessages = await context.IdempotencyRecords.ToListAsync();
            var orders = await context.Orders.ToListAsync();
            var duplicateAttempts = await context.DuplicateAttempts.CountAsync();

            // Log detailed statistics for debugging
            Console.WriteLine($"=== DETAILED STATISTICS CALCULATION ===");
            Console.WriteLine($"Total DeadLetterMessages: {messages.Count}");
            Console.WriteLine($"Total IdempotencyRecords: {processedMessages.Count}");
            Console.WriteLine($"Total Orders: {orders.Count}");
            Console.WriteLine($"Total DuplicateAttempts: {duplicateAttempts}");
            Console.WriteLine($"Completed Orders: {orders.Count(o => o.Status == OrderStatus.Completed)}");
            Console.WriteLine($"=======================================");

            return new DeadLetterQueueStats
            {
                TotalMessages = messages.Count,
                PendingMessages = pendingMessages.Count,
                ResolvedMessages = messages.Count(m => m.Status == DeadLetterStatus.Resolved),
                FailedMessages = messages.Count(m => m.Status == DeadLetterStatus.Failed),
                OldestMessageAge = pendingMessages.Any()
                    ? pendingMessages.Min(m => DateTime.UtcNow - m.FailureTimestamp)
                    : TimeSpan.Zero,

                // Additional statistics
                TotalProcessedMessages = processedMessages.Count,
                DuplicateMessagesDetected = duplicateAttempts,
                SuccessfulOrders = orders.Count(o => o.Status == OrderStatus.Completed),
                DeadLetterMessages = messages.Count
            };
        }
    }
}
