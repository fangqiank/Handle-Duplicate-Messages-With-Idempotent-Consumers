
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Services
{
    public interface IIIdempotencyService
    {
        Task<bool> CheckIfMessageAlreadyProcessedAsync(string messageId, string consumerName);
        Task<string?> GetProcessingResultAsync(string messageId, string consumerName);
        Task MarkMessageAsProcessedAsync(string messageId, string consumerName, string result);
    }
}