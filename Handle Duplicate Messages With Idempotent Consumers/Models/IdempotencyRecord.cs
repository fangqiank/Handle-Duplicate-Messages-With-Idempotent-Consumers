namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Models
{
    public class IdempotencyRecord
    {
        public string MessageId { get; set; } = string.Empty;
        public string ConsumerName { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public string Result { get; set; } = string.Empty;
        public bool IsProcessed { get; set; }
    }
}
