# Vali-Mediator

Vali-Mediator is a lightweight, zero-dependency mediator library for .NET 7, 8, and 9. It implements the CQRS pattern through requests, notifications (pub/sub), fire-and-forget commands, async streaming, pipeline behaviors, pre/post processors, Saga-pattern compensation flows, and a built-in `Result<T>` type. The library integrates seamlessly with `Microsoft.Extensions.DependencyInjection` and has no other external dependencies.

## Installation

```shell
dotnet add package Vali-Mediator
```

## Quick Start

```csharp
// Program.cs / Startup.cs
builder.Services.AddValiMediator(config =>
{
    // Register all handlers, processors, and stream handlers from an assembly
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Or register by assembly reference
    // config.RegisterServicesFromAssembly(typeof(Program).Assembly);
});
```

## Feature Overview

| Feature | Interface / Type | Description |
|---|---|---|
| Requests (query/command) | `IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>` | Single handler, request-response |
| Void requests | `IRequest`, `IRequestHandler<TRequest>` | Unit-returning shorthand |
| Notifications | `INotification`, `INotificationHandler<T>` | Pub/sub, multiple handlers |
| Fire-and-forget | `IFireAndForget`, `IFireAndForgetHandler<T>` | No response, side-effect commands |
| Streaming | `IStreamRequest<T>`, `IStreamRequestHandler<TRequest, T>` | `IAsyncEnumerable<T>` |
| Pipeline behaviors | `IPipelineBehavior<TRequest, TResponse>` | Cross-cutting concerns for requests |
| Dispatch behaviors | `IPipelineBehavior<TRequest>` | Cross-cutting concerns for notifications/fire-and-forget |
| Pre/Post processors | `IPreProcessor<T>`, `IPostProcessor<T>` | Hook before/after dispatch |
| Request processors | `IPreProcessor<TRequest, TResponse>`, `IPostProcessor<TRequest, TResponse>` | Hook before/after request handling |
| Result type | `Result<T>`, `Result` | Functional success/failure without exceptions |
| Compensation (Saga) | `ICompensable`, `Compensable` | Rollback pattern for distributed flows |

---

## Code Examples

### Request with Response

```csharp
// Define the request
public record GetUserQuery(int UserId) : IRequest<UserDto>;

// Define the handler
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        // ... fetch user
        return new UserDto(request.UserId, "Alice");
    }
}

// Send it
var user = await mediator.Send(new GetUserQuery(42));
```

### Void Request (Unit shorthand)

```csharp
public record DeleteUserCommand(int UserId) : IRequest;

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    public async Task<Unit> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        // delete user ...
        return Unit.Value;
    }
}

await mediator.Send(new DeleteUserCommand(42));
```

### Result<T> with Map/Bind chain

```csharp
public record CreateOrderCommand(int CustomerId, decimal Amount) : IRequest<Result<int>>;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<int>>
{
    public Task<Result<int>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
            return Task.FromResult(Result<int>.Fail("Amount must be positive.", ErrorType.Validation));

        return Task.FromResult(Result<int>.Ok(99)); // new order id
    }
}

// Usage with functional chaining
var result = await mediator.Send(new CreateOrderCommand(1, 100m));

var invoiceResult = result
    .Map(orderId => $"Invoice for order #{orderId}")
    .Tap(msg => Console.WriteLine(msg))
    .OnFailure((err, type) => Console.Error.WriteLine($"[{type}] {err}"));

string output = result.Match(
    onSuccess: id => $"Created order {id}",
    onFailure: (err, _) => $"Error: {err}"
);
```

### Result (non-generic) for void handlers

```csharp
public record ArchiveUserCommand(int UserId) : IRequest<Result>;

public class ArchiveUserHandler : IRequestHandler<ArchiveUserCommand, Result>
{
    public Task<Result> Handle(ArchiveUserCommand request, CancellationToken cancellationToken)
    {
        // ... attempt archive
        bool success = true;
        return Task.FromResult(success ? Result.Ok() : Result.Fail("User not found.", ErrorType.NotFound));
    }
}

var result = await mediator.Send(new ArchiveUserCommand(42));
result.Tap(() => Console.WriteLine("Archived"))
      .OnFailure((err, type) => Console.Error.WriteLine($"[{type}] {err}"));
```

### Notification with Priority

```csharp
public record OrderCreatedEvent(int OrderId) : INotification;

public class EmailNotificationHandler : INotificationHandler<OrderCreatedEvent>
{
    public int Priority => 10; // runs first
    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // send email
        return Task.CompletedTask;
    }
}

public class AuditLogHandler : INotificationHandler<OrderCreatedEvent>
{
    public int Priority => 5; // runs second
    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // write audit log
        return Task.CompletedTask;
    }
}

// Sequential (default) — respects priority order
await mediator.Publish(new OrderCreatedEvent(99));

// Parallel — all handlers concurrently
await mediator.Publish(new OrderCreatedEvent(99), PublishStrategy.Parallel);

// ResilientParallel — all run, failures collected as AggregateException
await mediator.Publish(new OrderCreatedEvent(99), PublishStrategy.ResilientParallel);
```

### Fire-and-Forget

```csharp
public record SendWelcomeEmailCommand(string Email) : IFireAndForget;

public class SendWelcomeEmailHandler : IFireAndForgetHandler<SendWelcomeEmailCommand>
{
    public Task Handle(SendWelcomeEmailCommand command, CancellationToken cancellationToken)
    {
        // send email asynchronously
        return Task.CompletedTask;
    }
}

await mediator.Send(new SendWelcomeEmailCommand("alice@example.com"));
```

### Pipeline Behavior

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var response = await next();
        _logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return response;
    }
}

// Registration — use generic shorthand (open-generic type required)
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>()
          .AddRequestBehavior<LoggingBehavior<,>>();
});
```

### Streaming

```csharp
public record GetProductsStream(string Category) : IStreamRequest<ProductDto>;

public class GetProductsStreamHandler : IStreamRequestHandler<GetProductsStream, ProductDto>
{
    public async IAsyncEnumerable<ProductDto> Handle(
        GetProductsStream request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < 100; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ProductDto(i, $"Product {i}");
            await Task.Delay(10, cancellationToken);
        }
    }
}

await foreach (var product in mediator.CreateStream(new GetProductsStream("Electronics")))
{
    Console.WriteLine(product.Name);
}
```

### Saga Compensation (ICompensable / Compensable)

```csharp
public class PlaceOrderCommand : Compensable, IRequest<Result<int>>
{
    public int CustomerId { get; init; }

    // Define the rollback action
    public override IFireAndForget? GetCompensation()
        => new CancelOrderCommand(CustomerId);
}

public record CancelOrderCommand(int CustomerId) : IFireAndForget;

// In a handler or orchestration:
var command = new PlaceOrderCommand { CustomerId = 1 };
try
{
    var result = await mediator.Send(command);
}
catch
{
    await command.Compensate(mediator);
}
```

### Pre/Post Processors

```csharp
// Pre-processor for a notification
public class OrderAuditPreProcessor : IPreProcessor<OrderCreatedEvent>
{
    public Task Process(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Before handling OrderCreatedEvent #{notification.OrderId}");
        return Task.CompletedTask;
    }
}

// Pre-processor for a request
public class ValidationPreProcessor<TRequest, TResponse> : IPreProcessor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task Process(TRequest request, CancellationToken cancellationToken)
    {
        // validate request before it reaches the handler
        return Task.CompletedTask;
    }
}
```

---

---

## Donations

If Vali-Mediator is useful to you, consider supporting its development:

- **Latin America** — [MercadoPago](https://link.mercadopago.com.pe/felipermm)
- **International** — [PayPal](https://paypal.me/felipeRMM?country.x=PE&locale.x=es_XC)

---

## License

[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)

## Contributions

Issues and pull requests are welcome on [GitHub](https://github.com/UBF21/Vali-Mediator).
