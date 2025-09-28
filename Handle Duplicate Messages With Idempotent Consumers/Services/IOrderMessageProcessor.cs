using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Services
{
    public interface IOrderMessageProcessor
    {
        Task<ProcessResult> ProcessMessageAsync(OrderMessage message);
    }
}