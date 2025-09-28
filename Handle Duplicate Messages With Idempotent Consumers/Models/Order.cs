using System.Reflection.PortableExecutable;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Models
{
    public class Order
    {
        public Guid Id { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
        public OrderStatus Status { get; set; }
    }

    public enum OrderStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }
}

