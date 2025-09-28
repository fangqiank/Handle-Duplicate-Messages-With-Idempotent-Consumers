namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Models
{
    public class DeadLetterMessage
    {
        public Guid Id { get; set; }
        public string OriginalMessageId { get; set; } = string.Empty;
        public string ConsumerName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime OriginalTimestamp { get; set; }
        public DateTime FailureTimestamp { get; set; }
        public int AttemptNumber { get; set; }
        public string FailureReason { get; set; } = string.Empty;
        public DeadLetterStatus Status { get; set; }
        public DateTime? ResolvedTimestamp { get; set; }
        public string? ResolutionNotes { get; set; }
    }

    public enum DeadLetterStatus
    {
        Pending,
        Resolved,
        Failed
    }
}