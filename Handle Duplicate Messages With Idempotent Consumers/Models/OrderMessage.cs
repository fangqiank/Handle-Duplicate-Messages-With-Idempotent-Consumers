namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Models
{
    public class OrderMessage
    {
        public string MessageId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; }
}
}
