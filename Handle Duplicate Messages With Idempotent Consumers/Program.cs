using Handle_Duplicate_Messages_With_Idempotent_Consumers.Data;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Models;
using Handle_Duplicate_Messages_With_Idempotent_Consumers.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseInMemoryDatabase("IdempotentConsumerDb")
);

builder.Services.AddScoped<IIdempotencyService, IIdempotencyService>();
builder.Services.AddScoped<IOrderMessageProcessor, OrderMessageProcessor>();
builder.Services.AddScoped<IdempotentConsumerService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/api/seed", async (AppDbContext context) =>
{
    // �����������
    context.Orders.RemoveRange(context.Orders);
    context.IdempotencyRecords.RemoveRange(context.IdempotencyRecords);
    await context.SaveChangesAsync();

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
})
.WithName("SeedData")
.WithOpenApi();

app.MapPost("/api/orders", async (OrderMessage message, IdempotentConsumerService service) =>
{

    if(string.IsNullOrEmpty(message.MessageId))
    {
        return Results.BadRequest("IdempotencyKey is required.");
    }

    var result = await service.ProcessMessageAsync(message);

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
    var records = await db.IdempotencyRecords.ToListAsync();

    return Results.Ok(records);
})
    .WithName("GetIdempotencyRecords")
    .WithOpenApi();

// Dead-letter queue management endpoints
app.MapGet("/api/dead-letter-queue", async (IIdempotencyService idempotencyService) =>
{
    var deadLetterMessages = await idempotencyService.GetDeadLetterMessagesAsync();
    var stats = await idempotencyService.GetDeadLetterQueueStatsAsync();

    return Results.Ok(new
    {
        Messages = deadLetterMessages,
        Stats = stats
    });
})
    .WithName("GetDeadLetterQueue")
    .WithOpenApi();

app.MapPost("/api/dead-letter-queue/{id}/retry", async (Guid id, IIdempotencyService idempotencyService) =>
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

using(var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Ensure the database is created
    dbContext.Database.EnsureCreated();
}

app.Run();

// ʹProgram��Բ��Կɼ�
public partial class Program { }
