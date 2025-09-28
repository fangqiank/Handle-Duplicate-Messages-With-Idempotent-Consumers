using Handle_Duplicate_Messages_With_Idempotent_Consumers.Data;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;
using Microsoft.EntityFrameworkCore;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Services
{
    public class IIdempotencyService(
        AppDbContext context,
        ILogger<IIdempotencyService> logger
        ) : IIIdempotencyService
    {
        private const int MaxProcessingAttempts = 3;
        private readonly Dictionary<string, int> _processingAttempts = new();

        public async Task<bool> CheckIfMessageAlreadyProcessedAsync(
            string messageId,
            string consumerName)
        {
            // Check if message is in dead-letter queue
            var deadLetterRecord = await context.DeadLetterMessages
                .FirstOrDefaultAsync(x => x.OriginalMessageId == messageId && x.Status == DeadLetterStatus.Pending);

            if (deadLetterRecord != null)
            {
                logger.LogWarning("Message {MessageId} found in dead-letter queue", messageId);
                return true; // Treat as already processed to prevent retry loops
            }

            var record = await context.IdempotencyRecords
                .FirstOrDefaultAsync(x => x.MessageId == messageId && x.ConsumerName == consumerName);

            return record?.IsProcessed ?? false;
        }

        public async Task<ProcessResult> ProcessWithDeadLetterQueueAsync(
            string messageId,
            string consumerName,
            Func<Task<ProcessResult>> processFunc)
        {
            // Check if already processed (standard idempotency)
            if (await CheckIfMessageAlreadyProcessedAsync(messageId, consumerName))
            {
                var existingResult = await GetProcessingResultAsync(messageId, consumerName);
                return ProcessResult.Ok($"Message already processed: {existingResult}", ExtractOrderIdFromResult(existingResult));
            }

            // Track processing attempts
            var attemptKey = $"{messageId}-{consumerName}";
            _processingAttempts.TryGetValue(attemptKey, out var currentAttempt);
            currentAttempt++;

            if (currentAttempt > MaxProcessingAttempts)
            {
                logger.LogWarning("Message {MessageId} exceeded max processing attempts, sending to dead-letter queue", messageId);
                await SendToDeadLetterQueueAsync(messageId, consumerName, currentAttempt, "Max processing attempts exceeded");
                return ProcessResult.Fail("Message exceeded max processing attempts");
            }

            _processingAttempts[attemptKey] = currentAttempt;

            try
            {
                logger.LogInformation("Processing message {MessageId} (attempt {AttemptNumber})", messageId, currentAttempt);

                var result = await processFunc();

                if (result.Success)
                {
                    await MarkMessageAsProcessedAsync(messageId, consumerName, $"OrderId: {result.OrderId}");
                    _processingAttempts.Remove(attemptKey); // Clear attempt counter on success
                    logger.LogInformation("Successfully processed message {MessageId} on attempt {AttemptNumber}", messageId, currentAttempt);
                }
                else
                {
                    logger.LogError("Message {MessageId} processing failed on attempt {AttemptNumber}: {Error}", messageId, currentAttempt, result.Error);

                    if (currentAttempt >= MaxProcessingAttempts)
                    {
                        await SendToDeadLetterQueueAsync(messageId, consumerName, currentAttempt, result.Error);
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
                    await SendToDeadLetterQueueAsync(messageId, consumerName, currentAttempt, ex.Message);
                    _processingAttempts.Remove(attemptKey);
                    return ProcessResult.Fail($"Message processing failed after {currentAttempt} attempts: {ex.Message}");
                }

                // For exceptions, we'll let the caller handle the retry
                throw;
            }
        }

        public async Task MarkMessageAsProcessedAsync(
            string messageId,
            string consumerName,
            string result)
        {
            var record = new IdempotencyRecord
            {
                MessageId = messageId,
                ConsumerName = consumerName,
                IsProcessed = true,
                Result = result,
            };

            context.IdempotencyRecords.Add(record);
            await context.SaveChangesAsync();

            logger.LogInformation("Marked message {MessageId} as processed by {ConsumerName}", messageId, consumerName);
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
            string failureReason)
        {
            try
            {
                var deadLetterRecord = new DeadLetterMessage
                {
                    Id = Guid.NewGuid(),
                    OriginalMessageId = messageId,
                    ConsumerName = consumerName,
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

            return new DeadLetterQueueStats
            {
                TotalMessages = messages.Count,
                PendingMessages = messages.Count(m => m.Status == DeadLetterStatus.Pending),
                ResolvedMessages = messages.Count(m => m.Status == DeadLetterStatus.Resolved),
                FailedMessages = messages.Count(m => m.Status == DeadLetterStatus.Failed),
                OldestMessageAge = messages.Where(m => m.Status == DeadLetterStatus.Pending)
                    .Min(m => DateTime.UtcNow - m.FailureTimestamp)
            };
        }
    }

    public class DeadLetterQueueStats
    {
        public int TotalMessages { get; set; }
        public int PendingMessages { get; set; }
        public int ResolvedMessages { get; set; }
        public int FailedMessages { get; set; }
        public TimeSpan? OldestMessageAge { get; set; }
    }
}
