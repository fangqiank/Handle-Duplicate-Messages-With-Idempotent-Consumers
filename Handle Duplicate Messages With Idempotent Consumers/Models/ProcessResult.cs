namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Models
{
    public class ProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Guid? OrderId { get; set; }
        public string Error { get; set; } = string.Empty;
        public static ProcessResult Ok(string message, Guid? orderId = null) =>
            new() { Success = true, Message = message, OrderId = orderId };

        public static ProcessResult Fail(string error) =>
            new() { Success = false, Error = error };
    }
}
