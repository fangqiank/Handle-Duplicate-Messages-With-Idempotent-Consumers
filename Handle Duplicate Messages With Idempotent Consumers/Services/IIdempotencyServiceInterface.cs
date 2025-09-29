using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Services
{
    public interface IIdempotencyServiceInterface
    {
        Task<bool> CheckIfMessageAlreadyProcessedAsync(string messageId, string consumerName);
        Task<string?> GetProcessingResultAsync(string messageId, string consumerName);
        Task MarkMessageAsProcessedAsync(string messageId, string consumerName, string result);
        Task<ProcessResult> ProcessWithDeadLetterQueueAsync(string messageId, string consumerName, Func<Task<ProcessResult>> processFunc, OrderMessage? orderMessage = null);
        Task<List<DeadLetterMessage>> GetDeadLetterMessagesAsync();
        Task<bool> RetryDeadLetterMessageAsync(Guid deadLetterMessageId);
        Task<DeadLetterQueueStats> GetDeadLetterQueueStatsAsync();
        Task MarkDuplicateDetectedAsync(string messageId, string consumerName, string source = "api");
    }
}