using Amazon.SQS.Model;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Data;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Services
{
    public class SqsMessageProcessor(
        AppDbContext dbContext,
        IIdempotencyService idempotencyService,
        SqsQueueService sqsQueueService,
        ILogger<SqsMessageProcessor> logger)
    {
        private readonly string _mainQueueUrl = string.Empty;
        private readonly string _dlqUrl = string.Empty;

        public async Task InitializeQueuesAsync(string queueName)
        {
            try
            {
                _mainQueueUrl = await sqsQueueService.CreateQueueWithDeadLetterQueueAsync(queueName);
                _dlqUrl = $"{_mainQueueUrl}-dlq"; // Simplified, in reality you'd get this from CreateQueue response
                logger.LogInformation("Initialized queues: Main={MainQueue}, DLQ={DlqUrl}", _mainQueueUrl, _dlqUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error initializing queues");
                throw;
            }
        }

        public async Task ProcessMessagesAsync(string queueUrl, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Starting message processing from queue: {QueueUrl}", queueUrl);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var message = await sqsQueueService.ReceiveMessageAsync(queueUrl);
                    if (message == null)
                    {
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }

                    await ProcessSingleMessageAsync(message, queueUrl);
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Message processing cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in message processing loop");
                    await Task.Delay(5000, cancellationToken); // Wait before retry
                }
            }
        }

        private async Task ProcessSingleMessageAsync(Message message, string queueUrl)
        {
            var messageId = GetMessageId(message);
            var receiveCount = GetReceiveCount(message);

            logger.LogInformation("Processing message {MessageId}, ReceiveCount: {ReceiveCount}", messageId, receiveCount);

            try
            {
                // Check if message has already been processed (idempotency check)
                if (await idempotencyService.CheckIfMessageAlreadyProcessedAsync(messageId, "sqs-processor"))
                {
                    logger.LogWarning("Message {MessageId} already processed, deleting from queue", messageId);
                    await sqsQueueService.DeleteMessageAsync(queueUrl, message.ReceiptHandle);
                    return;
                }

                // Parse and process the message
                var orderMessage = ParseMessage(message);
                if (orderMessage == null)
                {
                    logger.LogError("Failed to parse message {MessageId}", messageId);
                    // Don't delete, let it go to DLQ after max retries
                    return;
                }

                // Process the order message
                var result = await ProcessOrderMessageAsync(orderMessage);

                if (result.Success)
                {
                    // Mark as processed and delete from queue
                    await idempotencyService.MarkMessageAsProcessedAsync(
                        messageId,
                        "sqs-processor",
                        $"OrderId: {result.OrderId}");

                    await sqsQueueService.DeleteMessageAsync(queueUrl, message.ReceiptHandle);
                    logger.LogInformation("Successfully processed message {MessageId}", messageId);
                }
                else
                {
                    logger.LogError("Failed to process message {MessageId}: {Error}", messageId, result.Error);
                    // Don't delete, let it go to DLQ after max retries
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing message {MessageId}, ReceiveCount: {ReceiveCount}", messageId, receiveCount);

                if (receiveCount >= 3) // Max retries reached
                {
                    logger.LogWarning("Message {MessageId} reached max retries, will go to DLQ", messageId);
                    // Don't delete, let SQS move it to DLQ automatically
                }
            }
        }

        private async Task<ProcessResult> ProcessOrderMessageAsync(OrderMessage orderMessage)
        {
            try
            {
                // Simulate some processing time
                await Task.Delay(100);

                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerName = orderMessage.CustomerName,
                    Amount = orderMessage.Amount,
                    CreatedAt = DateTime.UtcNow,
                    Status = OrderStatus.Completed
                };

                dbContext.Orders.Add(order);
                await dbContext.SaveChangesAsync();

                logger.LogInformation("Order processed successfully: {OrderId}", order.Id);

                return ProcessResult.Ok("Order processed successfully", order.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing order for message {MessageId}", orderMessage.MessageId);
                return ProcessResult.Fail($"Error processing order: {ex.Message}");
            }
        }

        private string GetMessageId(Message message)
        {
            if (message.MessageAttributes.TryGetValue("MessageId", out var messageIdAttr))
            {
                return messageIdAttr.StringValue;
            }
            return message.MessageId ?? Guid.NewGuid().ToString();
        }

        private int GetReceiveCount(Message message)
        {
            if (message.Attributes.TryGetValue("ApproximateReceiveCount", out var receiveCountAttr))
            {
                return int.Parse(receiveCountAttr);
            }
            return 0;
        }

        private OrderMessage? ParseMessage(Message message)
        {
            try
            {
                return JsonSerializer.Deserialize<OrderMessage>(message.Body);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error parsing message body: {Body}", message.Body);
                return null;
            }
        }

        public async Task SendMessageToQueueAsync(OrderMessage orderMessage)
        {
            if (string.IsNullOrEmpty(_mainQueueUrl))
            {
                throw new InvalidOperationException("Queues not initialized. Call InitializeQueuesAsync first.");
            }

            var jsonMessage = JsonSerializer.Serialize(orderMessage);
            await sqsQueueService.SendMessageAsync(_mainQueueUrl, jsonMessage, orderMessage.MessageId);
        }

        public async Task StartDeadLetterQueueProcessorAsync(CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Starting dead-letter queue processor");

            // Process messages from DLQ
            await sqsQueueService.ProcessDeadLetterQueueAsync(_dlqUrl, async (message) =>
            {
                try
                {
                    var messageId = GetMessageId(message);
                    var receiveCount = GetReceiveCount(message);

                    logger.LogWarning("Processing DLQ message {MessageId}, ReceiveCount: {ReceiveCount}", messageId, receiveCount);

                    // Try to parse and process again
                    var orderMessage = ParseMessage(message);
                    if (orderMessage != null)
                    {
                        var result = await ProcessOrderMessageAsync(orderMessage);
                        if (result.Success)
                        {
                            await idempotencyService.MarkMessageAsProcessedAsync(
                                messageId,
                                "sqs-dlq-processor",
                                $"OrderId: {result.OrderId}");
                            return true; // Success, delete from DLQ
                        }
                    }

                    return false; // Failed, keep in DLQ
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing DLQ message");
                    return false;
                }
            });
        }
    }
}