using Handle_Duplicate_Messages_With_Idempotent_Consumers.Data;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Handle_Duplicate_Messages_With_Idempotent_Consumers.Test
{
    public class IdempotentConsumerTests: IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly IdempotentConsumerService _consumerService;
        private readonly AppDbContext _context;
        private readonly IIdempotencyService _idempotencyService;

        public IdempotentConsumerTests()
        {
            var services = new ServiceCollection();

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("TestDb"));

            services.AddLogging(builder => builder.AddConsole());

            services.AddScoped<IIdempotencyService, IIdempotencyService>();
            services.AddScoped<IOrderMessageProcessor, OrderMessageProcessor>();
            services.AddScoped<IdempotentConsumerService>();

            _serviceProvider = services.BuildServiceProvider();
            _consumerService = _serviceProvider.GetRequiredService<IdempotentConsumerService>();
            _context = _serviceProvider.GetRequiredService<AppDbContext>();
            _idempotencyService = _serviceProvider.GetRequiredService<IIdempotencyService>();

            _context.Database.EnsureCreated();
        }

        [Fact]
        public async Task ProcessMessage_FirstTime_ShouldSucceed()
        {
            var orderMessage = new OrderMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                CustomerName = "Test Customer",
                Amount = 100.50m,
                Timestamp = DateTime.UtcNow
            };


            var result = await _consumerService.ProcessMessageAsync(orderMessage);

            Assert.True(result.Success);
            Assert.NotNull(result.OrderId);
            Assert.NotEqual(Guid.Empty, result.OrderId.Value);
        }

        [Fact]
        public async Task ProcessMessage_Duplicate_ShouldReturnPreviousResult()
        {
            var messageId = Guid.NewGuid().ToString();
            var orderMessage = new OrderMessage
            {
                MessageId = messageId,
                CustomerName = "Test Customer",
                Amount = 100.50m,
                Timestamp = DateTime.UtcNow
            };

            var firstResult = await _consumerService.ProcessMessageAsync(orderMessage);
            var secondResult = await _consumerService.ProcessMessageAsync(orderMessage);
            
            Assert.True(firstResult.Success);
            Assert.True(secondResult.Success);
            Assert.Contains("already processed", secondResult.Message);
            Assert.Equal(firstResult.OrderId, secondResult.OrderId);
        }

        [Fact]
        public async Task ProcessMessage_WithEmptyMessageId_ShouldFail()
        {
            var orderMessage = new OrderMessage
            {
                MessageId = "", // Empty message id should fail
                CustomerName = "Test Customer",
                Amount = 100.50m,
                Timestamp = DateTime.UtcNow
            };

            var result = await _consumerService.ProcessMessageAsync(orderMessage);

            Assert.False(result.Success);
            Assert.Contains("MessageId is required", result.Error);
        }

        [Fact]
        public async Task ProcessMessage_DuplicateInDeadLetterQueue_ShouldPreventProcessing()
        {
            // First, manually add a message to dead-letter queue
            var deadLetterMessage = new DeadLetterMessage
            {
                Id = Guid.NewGuid(),
                OriginalMessageId = "dlq-test-message",
                ConsumerName = "order-processor",
                CustomerName = "Test Customer",
                Amount = 100.50m,
                OriginalTimestamp = DateTime.UtcNow,
                FailureTimestamp = DateTime.UtcNow,
                AttemptNumber = 3,
                FailureReason = "Test failure",
                Status = DeadLetterStatus.Pending
            };

            _context.DeadLetterMessages.Add(deadLetterMessage);
            await _context.SaveChangesAsync();

            // Try to process the same message
            var orderMessage = new OrderMessage
            {
                MessageId = "dlq-test-message",
                CustomerName = "Test Customer",
                Amount = 100.50m,
                Timestamp = DateTime.UtcNow
            };

            var result = await _consumerService.ProcessMessageAsync(orderMessage);

            // Should fail because message is in dead-letter queue
            Assert.False(result.Success);
            Assert.Contains("dead-letter queue", result.Error);
        }

        [Fact]
        public async Task GetDeadLetterQueueStats_ShouldReturnCorrectStats()
        {
            // Add some test data to dead-letter queue
            var deadLetterMessages = new List<DeadLetterMessage>
            {
                new DeadLetterMessage
                {
                    Id = Guid.NewGuid(),
                    OriginalMessageId = "msg1",
                    ConsumerName = "order-processor",
                    CustomerName = "Customer1",
                    Amount = 100m,
                    OriginalTimestamp = DateTime.UtcNow,
                    FailureTimestamp = DateTime.UtcNow,
                    AttemptNumber = 3,
                    FailureReason = "Failure1",
                    Status = DeadLetterStatus.Pending
                },
                new DeadLetterMessage
                {
                    Id = Guid.NewGuid(),
                    OriginalMessageId = "msg2",
                    ConsumerName = "order-processor",
                    CustomerName = "Customer2",
                    Amount = 200m,
                    OriginalTimestamp = DateTime.UtcNow,
                    FailureTimestamp = DateTime.UtcNow,
                    AttemptNumber = 2,
                    FailureReason = "Failure2",
                    Status = DeadLetterStatus.Pending
                },
                new DeadLetterMessage
                {
                    Id = Guid.NewGuid(),
                    OriginalMessageId = "msg3",
                    ConsumerName = "order-processor",
                    CustomerName = "Customer3",
                    Amount = 300m,
                    OriginalTimestamp = DateTime.UtcNow,
                    FailureTimestamp = DateTime.UtcNow,
                    AttemptNumber = 3,
                    FailureReason = "Failure3",
                    Status = DeadLetterStatus.Resolved,
                    ResolvedTimestamp = DateTime.UtcNow,
                    ResolutionNotes = "Manually resolved"
                }
            };

            _context.DeadLetterMessages.AddRange(deadLetterMessages);
            await _context.SaveChangesAsync();

            var stats = await _idempotencyService.GetDeadLetterQueueStatsAsync();

            Assert.Equal(3, stats.TotalMessages);
            Assert.Equal(2, stats.PendingMessages);
            Assert.Equal(1, stats.ResolvedMessages);
            Assert.Equal(0, stats.FailedMessages);
            Assert.NotNull(stats.OldestMessageAge);
        }

        [Fact]
        public async Task RetryDeadLetterMessage_ShouldSucceed()
        {
            // Add a message to dead-letter queue
            var deadLetterMessage = new DeadLetterMessage
            {
                Id = Guid.NewGuid(),
                OriginalMessageId = "retry-test-message",
                ConsumerName = "order-processor",
                CustomerName = "Test Customer",
                Amount = 100.50m,
                OriginalTimestamp = DateTime.UtcNow,
                FailureTimestamp = DateTime.UtcNow,
                AttemptNumber = 3,
                FailureReason = "Test failure",
                Status = DeadLetterStatus.Pending
            };

            _context.DeadLetterMessages.Add(deadLetterMessage);
            await _context.SaveChangesAsync();

            // Retry the dead-letter message
            var success = await _idempotencyService.RetryDeadLetterMessageAsync(deadLetterMessage.Id);

            Assert.True(success);

            // Verify the message was marked as resolved
            var updatedMessage = await _context.DeadLetterMessages.FindAsync(deadLetterMessage.Id);
            Assert.Equal(DeadLetterStatus.Resolved, updatedMessage.Status);
            Assert.NotNull(updatedMessage.ResolvedTimestamp);
            Assert.Equal("Ready for retry", updatedMessage.ResolutionNotes);
        }

        [Fact]
        public async Task RetryNonExistentDeadLetterMessage_ShouldFail()
        {
            var nonExistentId = Guid.NewGuid();
            var success = await _idempotencyService.RetryDeadLetterMessageAsync(nonExistentId);
            Assert.False(success);
        }

        [Fact]
        public async Task ProcessMessage_GetDeadLetterMessages_ShouldReturnPendingMessages()
        {
            // Clear existing data for this test
            _context.DeadLetterMessages.RemoveRange(_context.DeadLetterMessages);
            await _context.SaveChangesAsync();

            // Add test data to dead-letter queue
            var pendingMessage = new DeadLetterMessage
            {
                Id = Guid.NewGuid(),
                OriginalMessageId = "pending-msg-specific",
                ConsumerName = "order-processor",
                CustomerName = "Customer1",
                Amount = 100m,
                OriginalTimestamp = DateTime.UtcNow,
                FailureTimestamp = DateTime.UtcNow,
                AttemptNumber = 3,
                FailureReason = "Pending failure",
                Status = DeadLetterStatus.Pending
            };

            var resolvedMessage = new DeadLetterMessage
            {
                Id = Guid.NewGuid(),
                OriginalMessageId = "resolved-msg-specific",
                ConsumerName = "order-processor",
                CustomerName = "Customer2",
                Amount = 200m,
                OriginalTimestamp = DateTime.UtcNow,
                FailureTimestamp = DateTime.UtcNow,
                AttemptNumber = 3,
                FailureReason = "Resolved failure",
                Status = DeadLetterStatus.Resolved,
                ResolvedTimestamp = DateTime.UtcNow,
                ResolutionNotes = "Resolved"
            };

            _context.DeadLetterMessages.AddRange(pendingMessage, resolvedMessage);
            await _context.SaveChangesAsync();

            var deadLetterMessages = await _idempotencyService.GetDeadLetterMessagesAsync();

            Assert.Single(deadLetterMessages);
            Assert.Equal(pendingMessage.Id, deadLetterMessages.First().Id);
            Assert.Equal(DeadLetterStatus.Pending, deadLetterMessages.First().Status);
        }

        public void Dispose()
        {
            _context?.Dispose();
            _serviceProvider?.Dispose();
        }
    }
}
