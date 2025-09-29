using Handle_Duplicate_Messages_With_Idempotent_Consumers.Data;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;
using Microsoft.EntityFrameworkCore;

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

                // Check if order already exists (additional protection)
                var existingOrder = await context.Orders
                    .FirstOrDefaultAsync(o => o.CustomerName == message.CustomerName &&
                                            o.Amount == message.Amount &&
                                            o.CreatedAt > DateTime.UtcNow.AddSeconds(-10));

                if (existingOrder != null)
                {
                    logger.LogInformation("Order already exists for customer {CustomerName}, returning existing order: {OrderId}",
                        message.CustomerName, existingOrder.Id);
                    return ProcessResult.Ok("Order already processed", existingOrder.Id);
                }

                var record = new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerName = message.CustomerName,
                    Amount = message.Amount,
                    CreatedAt = DateTime.UtcNow,
                    Status = OrderStatus.Completed
                };

                try
                {
                    context.Orders.Add(record);
                    await context.SaveChangesAsync();
                    logger.LogInformation("Order processed successfully: {OrderId}", record.Id);
                }
                catch (Exception ex) when (ex.Message.Contains("same key has already been added"))
                {
                    // Handle concurrent order creation - find the existing order
                    var conflictingOrder = await context.Orders
                        .FirstOrDefaultAsync(o => o.CustomerName == message.CustomerName &&
                                                o.Amount == message.Amount &&
                                                o.CreatedAt > DateTime.UtcNow.AddSeconds(-10));

                    if (conflictingOrder != null)
                    {
                        logger.LogInformation("Detected concurrent order creation, returning existing order: {OrderId}", conflictingOrder.Id);
                        return ProcessResult.Ok("Order processed concurrently", conflictingOrder.Id);
                    }
                    else
                    {
                        throw; // Re-throw if we can't find the conflicting order
                    }
                }

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
