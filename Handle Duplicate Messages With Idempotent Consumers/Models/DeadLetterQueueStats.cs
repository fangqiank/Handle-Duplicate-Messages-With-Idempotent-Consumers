namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Models
{
    public class DeadLetterQueueStats
    {
        public int TotalMessages { get; set; }
        public int PendingMessages { get; set; }
        public int ResolvedMessages { get; set; }
        public int FailedMessages { get; set; }
        public TimeSpan? OldestMessageAge { get; set; }

        // Duplicate message statistics
        public int TotalProcessedMessages { get; set; }
        public int DuplicateMessagesDetected { get; set; }
        public int SuccessfulOrders { get; set; }
        public int DeadLetterMessages { get; set; }
    }

    public class DuplicateAttempt
    {
        public Guid Id { get; set; }
        public string MessageId { get; set; }
        public string ConsumerName { get; set; }
        public DateTime DetectedAt { get; set; }
        public string Source { get; set; } // "api", "ui", etc.
    }
}
