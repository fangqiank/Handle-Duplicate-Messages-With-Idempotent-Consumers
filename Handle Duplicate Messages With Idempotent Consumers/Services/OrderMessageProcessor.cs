using Handle_Duplicate_Messages_With_Idempotent_Consumers.Data;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Services
{
    public class OrderMessageProcessor(
        AppDbContext context,
        ILogger<OrderMessageProcessor> logger
        ) : IOrderMessageProcessor
    {
        public async Task<ProcessResult> ProcessMessageAsync(OrderMessage message)
        {
            try
            {
                await Task.Delay(100); // Simulate some processing time

                var record = new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerName = message.CustomerName,
                    Amount = message.Amount,
                    CreatedAt = DateTime.UtcNow,
                    Status = OrderStatus.Completed
                };

                context.Orders.Add(record);
                await context.SaveChangesAsync();

                logger.LogInformation("Order processed successfully: {OrderId}", record.Id);

                return ProcessResult.Ok("Order processed successfully", record.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing order message: {MessageId}", message.MessageId);

                return ProcessResult.Fail($"Error processing order: {ex.Message}");
            }
        }
    }
}
