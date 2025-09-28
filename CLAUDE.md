# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 9.0 ASP.NET Core Web API demonstrating idempotent consumer pattern for handling duplicate messages. The application prevents duplicate processing of messages by tracking processed messages in an in-memory database.

## Build and Development Commands

### Building and Running
```bash
# Build the solution
dotnet build

# Run the application
dotnet run --project "Handle Duplicate Messages With Idempotent Consumers"

# Run in watch mode for development
dotnet watch --project "Handle Duplicate Messages With Idempotent Consumers"
```

### Testing
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test "Handle Duplicate Messages With Idempotent Consumers"
```

### Database Operations
The application uses Entity Framework Core with an in-memory database. The database is automatically created on application startup.

## Architecture

### Core Components

**IdempotentConsumerService** (`Services/IdempotentConsumerService.cs`): Main service that implements the idempotent consumer pattern. It checks if a message has been processed before, processes it if not, and stores the result.

**IIdempotencyService** (`Services/IIdempotencyService.cs`): Service responsible for tracking processed messages and storing/retrieving processing results.

**OrderMessageProcessor** (`Services/OrderMessageProcessor.cs`): Business logic processor that handles the actual message processing (creating orders).

### Data Models

**OrderMessage** (`Models/OrderMessage.cs`): Incoming message format with MessageId, CustomerName, Amount, and Timestamp.

**Order** (`Models/Order.cs`): Entity representing processed orders with status tracking.

**IdempotencyRecord** (`Models/IdempotencyRecord.cs`): Entity tracking processed messages with composite key (MessageId, ConsumerName).

**ProcessResult** (`Models/ProcessResult.cs`): Result object returned by message processing operations.

### Database Context

**AppDbContext** (`Data/AppDbContext.cs`): Entity Framework context with DbSets for Orders and IdempotencyRecords. Uses in-memory database and defines composite key for IdempotencyRecords.

### API Endpoints

- `POST /api/orders` - Process order messages with idempotency
- `POST /api/seed` - Initialize sample data for testing
- `GET /api/orders` - Retrieve idempotency records
- `GET /health` - Health check endpoint

## Key Patterns

### Idempotent Consumer Pattern
The core pattern prevents duplicate message processing by:
1. Using MessageId as idempotency key
2. Checking if message was already processed
3. Returning cached result for duplicates
4. Only processing new messages

### Service Layer Architecture
Clean separation with:
- Service interfaces for testability
- Dependency injection throughout
- Repository pattern via EF Core
- Result pattern for operation outcomes

## Testing

**IdempotentConsumerTests** (`Test/IdempotentConsumerTests.cs`): Unit tests covering:
- First-time message processing
- Duplicate message handling
- Error scenarios

Tests use in-memory database and service collection setup matching the main application.

## Configuration

- Uses .NET 9.0 with nullable reference types enabled
- Entity Framework Core InMemory database provider
- OpenAPI/Swagger documentation enabled
- Development-time HTTPS redirection