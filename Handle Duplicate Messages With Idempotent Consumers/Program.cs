using Handle_Duplicate_Messages_With_Idempotent_Consumers.Data;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add CORS services
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});


builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseInMemoryDatabase("IdempotentConsumerDb")
);

builder.Services.AddScoped<IIdempotencyServiceInterface, IIdempotencyService>();
builder.Services.AddScoped<IOrderMessageProcessor, OrderMessageProcessor>();
builder.Services.AddScoped<IdempotentConsumerService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Enable CORS
app.UseCors("AllowAll");

app.UseHttpsRedirection();

// Enable static files
app.UseStaticFiles();

// Default route to serve the frontend
app.MapGet("/", () => {
    var filePath = Path.Combine(builder.Environment.WebRootPath, "index.html");
    return Results.File(filePath, "text/html");
});

// DLQ test guide route
app.MapGet("/dlq-test-guide.html", () => {
    var filePath = Path.Combine(builder.Environment.WebRootPath, "dlq-test-guide.html");
    return Results.File(filePath, "text/html");
});

app.MapPost("/api/seed", async (AppDbContext context) =>
{
    // Only clear data if this is the first run
    var hasExistingData = await context.IdempotencyRecords.AnyAsync();

    if (!hasExistingData)
    {
        // ����ʾ������
        var sampleOrder = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = "Sample Customer",
            Amount = 99.99m,
            CreatedAt = DateTime.UtcNow,
            Status = OrderStatus.Completed
        };
        context.Orders.Add(sampleOrder);

        // ����ʾ���ݵȼ�¼
        var sampleRecord = new IdempotencyRecord
        {
            MessageId = "sample-message-123",
            ConsumerName = "order-processor",
            ProcessedAt = DateTime.UtcNow,
            Result = "OrderId: " + sampleOrder.Id,
            IsProcessed = true
        };
        context.IdempotencyRecords.Add(sampleRecord);

        await context.SaveChangesAsync();

        return Results.Ok(new { message = "Sample data initialized" });
    }
    else
    {
        return Results.Ok(new { message = "Sample data already exists" });
    }
})
.WithName("SeedData")
.WithOpenApi();

// Test endpoint for idempotency demo
app.MapPost("/api/test-idempotency", async (AppDbContext context, IIdempotencyServiceInterface idempotencyService) =>
{
    var testMessageId = "test-duplicate-message-" + DateTime.UtcNow.Ticks;

    // First attempt - should succeed
    var firstResult = await idempotencyService.ProcessWithDeadLetterQueueAsync(
        testMessageId,
        "test-consumer",
        async () => {
            await Task.Delay(100); // Simulate processing
            return ProcessResult.Ok("First processing successful", Guid.NewGuid());
        });

    // Second attempt with same MessageId - should return cached result
    var secondResult = await idempotencyService.ProcessWithDeadLetterQueueAsync(
        testMessageId,
        "test-consumer",
        async () => {
            await Task.Delay(100); // This should NOT be executed
            return ProcessResult.Ok("This should not appear", Guid.NewGuid());
        });

    return Results.Ok(new
    {
        TestMessageId = testMessageId,
        FirstAttempt = new { success = firstResult.Success, message = firstResult.Message, orderId = firstResult.OrderId },
        SecondAttempt = new { success = secondResult.Success, message = secondResult.Message, orderId = secondResult.OrderId },
        IsIdempotent = firstResult.OrderId == secondResult.OrderId && secondResult.Message.Contains("already processed")
    });
})
.WithName("TestIdempotency")
.WithOpenApi();

// Clear all data
app.MapPost("/api/clear", async (AppDbContext context) =>
{
    context.Orders.RemoveRange(context.Orders);
    context.IdempotencyRecords.RemoveRange(context.IdempotencyRecords);
    context.DeadLetterMessages.RemoveRange(context.DeadLetterMessages);
    context.DuplicateAttempts.RemoveRange(context.DuplicateAttempts);
    await context.SaveChangesAsync();

    return Results.Ok(new { message = "All data cleared" });
})
.WithName("ClearData")
.WithOpenApi();

// Test DLQ endpoint - creates a message that will fail processing
app.MapPost("/api/test-dlq", async (OrderMessage message, IIdempotencyServiceInterface idempotencyService, AppDbContext context) =>
{
    // Debug logging
    Console.WriteLine($"Testing DLQ with message: MessageId={message.MessageId}, CustomerName={message.CustomerName}, Amount={message.Amount}");

    if (string.IsNullOrEmpty(message.MessageId))
    {
        return Results.BadRequest("MessageId is required.");
    }

    // Use a special processing function that always fails
    var result = await idempotencyService.ProcessWithDeadLetterQueueAsync(
        message.MessageId,
        "dlq-test-consumer",
        async () => {
            // Simulate processing that always fails
            await Task.Delay(100);
            return ProcessResult.Fail("Simulated processing failure for DLQ test");
        },
        message);

    // Force immediate DLQ by sending it directly if it failed
    if (!result.Success)
    {
        Console.WriteLine($"Processing failed, forcing DLQ creation for message: {message.MessageId}");

        // Create DLQ record directly for testing
        var dlqMessage = new DeadLetterMessage
        {
            Id = Guid.NewGuid(),
            OriginalMessageId = message.MessageId,
            CustomerName = message.CustomerName,
            Amount = message.Amount,
            OriginalTimestamp = DateTime.UtcNow,
            AttemptNumber = 1,
            FailureReason = "Forced DLQ test failure",
            FailureTimestamp = DateTime.UtcNow,
            Status = DeadLetterStatus.Pending
        };

        context.DeadLetterMessages.Add(dlqMessage);
        await context.SaveChangesAsync();
        Console.WriteLine($"DLQ record created for message: {message.MessageId}");
    }

    return Results.Ok(new {
        success = true,
        message = result.Message,
        wasSentToDLQ = !result.Success
    });
})
.WithName("TestDLQ")
.WithOpenApi();

// Message queue API
app.MapGet("/api/message-queue", async (AppDbContext context) =>
{
    var idempotencyRecords = await context.IdempotencyRecords
        .OrderByDescending(r => r.ProcessedAt)
        .Take(50)
        .ToListAsync();

    var messageQueueData = idempotencyRecords.Select(record => new
    {
        record.MessageId,
        record.ConsumerName,
        Status = record.IsProcessed ? "Processed" : "Processing",
        record.ProcessedAt,
        record.Result,
        Attempts = record.Result.Contains("attempt") ?
            ExtractAttemptsFromResult(record.Result) : 1
    }).ToList();

    return Results.Ok(messageQueueData);
})
.WithName("GetMessageQueue")
.WithOpenApi();

app.MapPost("/api/orders", async (OrderMessage message, IdempotentConsumerService service) =>
{
    // Debug logging
    Console.WriteLine($"=== ORDER RECEIVED ===");
    Console.WriteLine($"MessageId: {message.MessageId}");
    Console.WriteLine($"CustomerName: {message.CustomerName}");
    Console.WriteLine($"Amount: {message.Amount}");
    Console.WriteLine($"===================");

    if(string.IsNullOrEmpty(message.MessageId))
    {
        return Results.BadRequest("IdempotencyKey is required.");
    }

    var result = await service.ProcessMessageAsync(message);

    Console.WriteLine($"=== ORDER RESULT ===");
    Console.WriteLine($"Success: {result.Success}");
    Console.WriteLine($"Message: {result.Message}");
    Console.WriteLine($"OrderId: {result.OrderId}");
    Console.WriteLine($"Error: {result.Error}");
    Console.WriteLine("====================");

    return result.Success
        ? Results.Ok(new
        {
            success = true,
            message = result.Message,
            orderId = result.OrderId
        })
        : Results.BadRequest(new
        {
            success = false,
            error = result.Error
        });
})
    .WithName("CreateOrder")
    .WithOpenApi();

app.MapGet("/api/orders", async (AppDbContext db) => {
    var orders = await db.Orders.ToListAsync();

    return Results.Ok(orders);
})
    .WithName("GetIdempotencyRecords")
    .WithOpenApi();

// Dead-letter queue management endpoints
app.MapGet("/api/dead-letter-queue", async (IIdempotencyServiceInterface idempotencyService) =>
{
    var deadLetterMessages = await idempotencyService.GetDeadLetterMessagesAsync();
    var stats = await idempotencyService.GetDeadLetterQueueStatsAsync();

    // Log statistics to console for debugging
    Console.WriteLine("=== BACKEND CONSOLE STATISTICS ===");
    Console.WriteLine($"Total Processed Messages: {stats.TotalProcessedMessages}");
    Console.WriteLine($"Duplicate Messages Detected: {stats.DuplicateMessagesDetected}");
    Console.WriteLine($"Successful Orders: {stats.SuccessfulOrders}");
    Console.WriteLine($"Failed Messages: {stats.FailedMessages}");
    Console.WriteLine($"Dead Letter Messages: {stats.DeadLetterMessages}");
    Console.WriteLine($"===================================");

    return Results.Ok(new
    {
        Messages = deadLetterMessages,
        Stats = stats
    });
})
    .WithName("GetDeadLetterQueue")
    .WithOpenApi();

app.MapPost("/api/dead-letter-queue/{id}/retry", async (Guid id, IIdempotencyServiceInterface idempotencyService) =>
{
    var success = await idempotencyService.RetryDeadLetterMessageAsync(id);

    return success
        ? Results.Ok(new { message = "Dead-letter message marked for retry" })
        : Results.NotFound(new { error = "Dead-letter message not found" });
})
    .WithName("RetryDeadLetterMessage")
    .WithOpenApi();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTime.UtcNow
}))
    .WithName("HealthCheck")
    .WithOpenApi();

// Test duplicate detection statistics
app.MapPost("/api/test-duplicate-stats", async (IIdempotencyServiceInterface idempotencyService) =>
{
    var testMessageId = "duplicate-stats-test-" + DateTime.UtcNow.Ticks;

    Console.WriteLine($"=== RAPID TEST DEBUG ===");
    Console.WriteLine($"Testing with MessageId: {testMessageId}");

    // First attempt - should succeed
    var firstResult = await idempotencyService.ProcessWithDeadLetterQueueAsync(
        testMessageId,
        "test-consumer",
        async () => {
            await Task.Delay(50);
            return ProcessResult.Ok("Test processing successful", Guid.NewGuid());
        });

    Console.WriteLine($"First attempt result: {firstResult.Success}, Message: {firstResult.Message}");

    // Second attempt - should be detected as duplicate
    var secondResult = await idempotencyService.ProcessWithDeadLetterQueueAsync(
        testMessageId,
        "test-consumer",
        async () => {
            await Task.Delay(50);
            return ProcessResult.Ok("This should not execute", Guid.NewGuid());
        });

    Console.WriteLine($"Second attempt result: {secondResult.Success}, Message: {secondResult.Message}");

    // Get statistics to see if duplicate was counted
    var stats = await idempotencyService.GetDeadLetterQueueStatsAsync();

    Console.WriteLine($"=== RAPID TEST STATISTICS ===");
    Console.WriteLine($"Total Processed Messages: {stats.TotalProcessedMessages}");
    Console.WriteLine($"Duplicate Messages Detected: {stats.DuplicateMessagesDetected}");
    Console.WriteLine($"Successful Orders: {stats.SuccessfulOrders}");
    Console.WriteLine($"Is Working: {secondResult.Message.Contains("already processed") && stats.DuplicateMessagesDetected > 0}");
    Console.WriteLine($"=============================");

    return Results.Ok(new
    {
        TestMessageId = testMessageId,
        FirstAttempt = new { success = firstResult.Success, message = firstResult.Message },
        SecondAttempt = new { success = secondResult.Success, message = secondResult.Message },
        Statistics = new
        {
            TotalProcessedMessages = stats.TotalProcessedMessages,
            DuplicateMessagesDetected = stats.DuplicateMessagesDetected,
            SuccessfulOrders = stats.SuccessfulOrders
        },
        IsWorking = secondResult.Message.Contains("already processed") && stats.DuplicateMessagesDetected > 0
    });
})
    .WithName("TestDuplicateStats")
    .WithOpenApi();

// Check database records for debugging
app.MapGet("/api/debug-records", async (AppDbContext context) =>
{
    var records = await context.IdempotencyRecords
        .OrderByDescending(r => r.ProcessedAt)
        .Take(10)
        .Select(r => new
        {
            r.MessageId,
            r.ConsumerName,
            r.ProcessedAt,
            r.Result,
            r.IsProcessed,
            HasDuplicateMarker = r.Result.Contains("[DUPLICATE DETECTED]")
        })
        .ToListAsync();

    var duplicateCount = records.Count(r => r.HasDuplicateMarker);
    var totalCount = records.Count;

    return Results.Ok(new
    {
        TotalRecords = totalCount,
        RecordsWithDuplicateMarker = duplicateCount,
        RecentRecords = records
    });
})
    .WithName("DebugRecords")
    .WithOpenApi();

// Check duplicate attempts for debugging
app.MapGet("/api/debug-duplicates", async (AppDbContext context) =>
{
    var duplicateAttempts = await context.DuplicateAttempts
        .OrderByDescending(d => d.DetectedAt)
        .Take(10)
        .ToListAsync();

    var totalCount = await context.DuplicateAttempts.CountAsync();

    return Results.Ok(new
    {
        TotalDuplicateAttempts = totalCount,
        RecentAttempts = duplicateAttempts
    });
})
    .WithName("DebugDuplicates")
    .WithOpenApi(); 

using(var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Ensure the database is created
    dbContext.Database.EnsureCreated();
}

// Helper function to extract attempt count from result message
int ExtractAttemptsFromResult(string result)
{
    var match = Regex.Match(result, @"attempt (\d+)");
    return match.Success ? int.Parse(match.Groups[1].Value) : 1;
}

app.Run();

// ʹProgram��Բ��Կɼ�
public partial class Program { }
