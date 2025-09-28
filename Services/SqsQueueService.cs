using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Services
{
    public class SqsQueueService(
        IAmazonSQS sqsClient,
        ILogger<SqsQueueService> logger)
    {
        private const int MaxRetries = 3;
        private const int VisibilityTimeoutSeconds = 30;

        public async Task<string> CreateQueueWithDeadLetterQueueAsync(string queueName)
        {
            try
            {
                // First create the dead-letter queue
                var dlqName = $"{queueName}-dlq";
                var dlqResponse = await sqsClient.CreateQueueAsync(dlqName);
                var dlqArn = await GetQueueArnAsync(dlqResponse.QueueUrl);

                // Create the main queue with dead-letter queue configuration
                var createQueueRequest = new CreateQueueRequest
                {
                    QueueName = queueName,
                    Attributes = new Dictionary<string, string>
                    {
                        { "VisibilityTimeout", VisibilityTimeoutSeconds.ToString() },
                        { "ReceiveMessageWaitTimeSeconds", "20" }, // Long polling
                        { "RedrivePolicy", $"{{\"deadLetterTargetArn\":\"{dlqArn}\",\"maxReceiveCount\":\"{MaxRetries}\"}}" }
                    }
                };

                var response = await sqsClient.CreateQueueAsync(createQueueRequest);
                logger.LogInformation("Created queue {QueueName} with DLQ {DlqName}", queueName, dlqName);

                return response.QueueUrl;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating queue {QueueName}", queueName);
                throw;
            }
        }

        public async Task SendMessageAsync(string queueUrl, string messageBody, string messageId)
        {
            try
            {
                var request = new SendMessageRequest
                {
                    QueueUrl = queueUrl,
                    MessageBody = messageBody,
                    MessageGroupId = messageId, // For FIFO queues
                    MessageDeduplicationId = messageId, // For deduplication
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        { "MessageId", new MessageAttributeValue { DataType = "String", StringValue = messageId } },
                        { "Timestamp", new MessageAttributeValue { DataType = "String", StringValue = DateTime.UtcNow.ToString("O") } }
                    }
                };

                await sqsClient.SendMessageAsync(request);
                logger.LogInformation("Sent message {MessageId} to queue", messageId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sending message {MessageId} to queue", messageId);
                throw;
            }
        }

        public async Task<Message?> ReceiveMessageAsync(string queueUrl)
        {
            try
            {
                var request = new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = 1,
                    WaitTimeSeconds = 20, // Long polling
                    AttributeNames = new List<string> { "All" },
                    MessageAttributeNames = new List<string> { "All" }
                };

                var response = await sqsClient.ReceiveMessageAsync(request);

                if (response.Messages.Any())
                {
                    var message = response.Messages.First();
                    logger.LogInformation("Received message {MessageId} from queue",
                        message.MessageAttributes.TryGetValue("MessageId", out var messageIdAttr)
                            ? messageIdAttr.StringValue
                            : "Unknown");
                    return message;
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error receiving message from queue");
                throw;
            }
        }

        public async Task DeleteMessageAsync(string queueUrl, string receiptHandle)
        {
            try
            {
                await sqsClient.DeleteMessageAsync(queueUrl, receiptHandle);
                logger.LogDebug("Deleted message from queue");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting message from queue");
                throw;
            }
        }

        public async Task<int> GetApproximateNumberOfMessagesAsync(string queueUrl)
        {
            try
            {
                var attributes = await sqsClient.GetQueueAttributesAsync(queueUrl, new List<string> { "ApproximateNumberOfMessages" });
                return int.Parse(attributes.ApproximateNumberOfMessages);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting message count from queue");
                return 0;
            }
        }

        private async Task<string> GetQueueArnAsync(string queueUrl)
        {
            var attributes = await sqsClient.GetQueueAttributesAsync(queueUrl, new List<string> { "QueueArn" });
            return attributes.QueueArn;
        }

        public async Task ProcessDeadLetterQueueAsync(string dlqUrl, Func<Message, Task<bool>> messageHandler)
        {
            logger.LogInformation("Starting dead-letter queue processing");

            while (true)
            {
                var message = await ReceiveMessageAsync(dlqUrl);
                if (message == null)
                {
                    await Task.Delay(5000); // Wait before next poll
                    continue;
                }

                try
                {
                    var success = await messageHandler(message);
                    if (success)
                    {
                        await DeleteMessageAsync(dlqUrl, message.ReceiptHandle);
                        logger.LogInformation("Successfully processed message from DLQ");
                    }
                    else
                    {
                        logger.LogWarning("Failed to process message from DLQ, leaving in queue for retry");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing message from DLQ");
                }
            }
        }
    }
}