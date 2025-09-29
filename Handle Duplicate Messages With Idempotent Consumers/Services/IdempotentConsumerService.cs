using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Services
{
    public class IdempotentConsumerService(
        IIdempotencyServiceInterface idempotencyService,
        IOrderMessageProcessor messageProcessor,
        ILogger<IdempotentConsumerService> logger
        )
    {
        public async Task<ProcessResult> ProcessMessageAsync(
            OrderMessage message,
            string consumerName = "order-processor"
            )
        {
            // Validate MessageId
            if (string.IsNullOrEmpty(message.MessageId))
            {
                logger.LogWarning("MessageId is required");
                return ProcessResult.Fail("MessageId is required");
            }

            // Use the enhanced dead-letter queue processing method
            return await idempotencyService.ProcessWithDeadLetterQueueAsync(
                message.MessageId,
                consumerName,
                async () =>
                {
                    // Process the message with retry logic and dead-letter queue support
                    var result = await messageProcessor.ProcessMessageAsync(message);

                    if (result.Success)
                    {
                        logger.LogInformation("Order processed successfully: {OrderId}", result.OrderId);
                    }
                    else
                    {
                        logger.LogError("Order processing failed: {Error}", result.Error);
                    }

                    return result;
                },
                message); // Pass the order message for dead-letter queue context
        }

        private Guid? ExtractOrderIdFromResult(string? result)
        {
            if (string.IsNullOrEmpty(result))
                return null;

            // Parse "OrderId: guid" format
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
    }
}
