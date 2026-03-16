# Processors

Processors are lightweight hooks that run before or after a handler without requiring the full pipeline behavior infrastructure. They are discovered automatically from the registered assembly.

---

## Two Processor Types

| Interface | When it runs | Applies to |
|---|---|---|
| `IPreProcessor<TDispatch>` | Before the handler | `INotification`, `IFireAndForget` |
| `IPreProcessor<TRequest, TResponse>` | Before the handler | `IRequest<TResponse>` |
| `IPostProcessor<TDispatch>` | After the handler | `INotification`, `IFireAndForget` |
| `IPostProcessor<TRequest, TResponse>` | After the handler | `IRequest<TResponse>` |

---

## IPreProcessor\<TDispatch\>

For dispatch types (`INotification`, `IFireAndForget`):

```csharp
using Vali_Mediator.Core.Processors;

// Runs before every OrderPlacedNotification handler
public class OrderPlacedAuditPreProcessor : IPreProcessor<OrderPlacedNotification>
{
    private readonly IAuditService _audit;

    public OrderPlacedAuditPreProcessor(IAuditService audit) => _audit = audit;

    public async Task Process(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _audit.RecordAsync(
            "ORDER_PLACED_NOTIFICATION_DISPATCHED",
            notification.OrderId.ToString(),
            ct);
    }
}

// Runs before every fire-and-forget email send
public class EmailLoggingPreProcessor : IPreProcessor<SendWelcomeEmailCommand>
{
    private readonly ILogger<EmailLoggingPreProcessor> _logger;

    public EmailLoggingPreProcessor(ILogger<EmailLoggingPreProcessor> logger)
        => _logger = logger;

    public Task Process(SendWelcomeEmailCommand command, CancellationToken ct)
    {
        _logger.LogDebug("Sending welcome email to {Email}.", command.Email);
        return Task.CompletedTask;
    }
}
```

---

## IPreProcessor\<TRequest, TResponse\>

For request/response types:

```csharp
// Runs before CreateOrderCommand is handled
public class CreateOrderPreProcessor : IPreProcessor<CreateOrderCommand, Result<Guid>>
{
    private readonly ILogger<CreateOrderPreProcessor> _logger;
    private readonly ICurrentUser _currentUser;

    public CreateOrderPreProcessor(
        ILogger<CreateOrderPreProcessor> logger,
        ICurrentUser currentUser)
    {
        _logger = logger;
        _currentUser = currentUser;
    }

    public Task Process(CreateOrderCommand request, CancellationToken ct)
    {
        _logger.LogInformation(
            "User {UserId} is creating an order with {ItemCount} items.",
            _currentUser.UserId, request.Items.Count);

        return Task.CompletedTask;
    }
}
```

---

## IPostProcessor\<TDispatch\>

For dispatch types — runs after all handlers have completed:

```csharp
// Runs after every OrderPlacedNotification handler
public class OrderPlacedMetricsPostProcessor : IPostProcessor<OrderPlacedNotification>
{
    private readonly IMetricsService _metrics;

    public OrderPlacedMetricsPostProcessor(IMetricsService metrics) => _metrics = metrics;

    public async Task Process(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _metrics.IncrementAsync("notifications.order_placed", ct);
    }
}
```

---

## IPostProcessor\<TRequest, TResponse\>

For request/response types — receives both the original request and the handler's response:

```csharp
// Runs after CreateOrderCommand is handled — logs the result
public class CreateOrderPostProcessor : IPostProcessor<CreateOrderCommand, Result<Guid>>
{
    private readonly ILogger<CreateOrderPostProcessor> _logger;

    public CreateOrderPostProcessor(ILogger<CreateOrderPostProcessor> logger)
        => _logger = logger;

    public Task Process(CreateOrderCommand request, Result<Guid> response, CancellationToken ct)
    {
        if (response.IsSuccess)
            _logger.LogInformation("Order {OrderId} created successfully.", response.Value);
        else
            _logger.LogWarning("Failed to create order: {Error} ({ErrorType}).",
                response.Error, response.ErrorType);

        return Task.CompletedTask;
    }
}

// Invalidate cache after a product is updated
public class UpdateProductCachePostProcessor
    : IPostProcessor<UpdateProductCommand, Result>
{
    private readonly ICacheService _cache;

    public UpdateProductCachePostProcessor(ICacheService cache) => _cache = cache;

    public async Task Process(UpdateProductCommand request, Result response, CancellationToken ct)
    {
        if (response.IsSuccess)
            await _cache.InvalidateAsync($"product:{request.ProductId}", ct);
    }
}
```

---

## Auto-Discovery

Processors implementing any of the four interfaces are **automatically discovered** when `RegisterServicesFromAssembly` scans the assembly. No explicit registration is required:

```csharp
builder.Services.AddValiMediator(config =>
{
    // All IPreProcessor<> and IPostProcessor<> implementations in this assembly
    // are discovered and registered automatically
    config.RegisterServicesFromAssemblyContaining<Program>();
});
```

---

## Explicit Registration

If you need to register a processor from a different assembly or with a specific lifetime, use the explicit methods:

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Explicit registration for a specific dispatch type
    config.AddPreProcessor(
        typeof(IPreProcessor<OrderPlacedNotification>),
        typeof(OrderPlacedAuditPreProcessor));

    // Explicit registration for a specific request type
    config.AddRequestPreProcessor(
        typeof(IPreProcessor<CreateOrderCommand, Result<Guid>>),
        typeof(CreateOrderPreProcessor));

    config.AddRequestPostProcessor(
        typeof(IPostProcessor<CreateOrderCommand, Result<Guid>>),
        typeof(CreateOrderPostProcessor),
        ServiceLifetime.Singleton);
});
```

---

## Processors vs Behaviors

| Aspect | Processors | Behaviors |
|---|---|---|
| Interface | `IPreProcessor<T>`, `IPostProcessor<T>` | `IPipelineBehavior<TRequest, TResponse>` |
| Position | Before or after (separate hooks) | Wraps the entire call (before + after in one class) |
| Short-circuit | No — processors cannot stop the pipeline | Yes — by not calling `next()` |
| Auto-discovery | Yes | No — always registered explicitly |
| Access to response | Only `IPostProcessor<TRequest, TResponse>` | Yes — full control |
| Best for | Simple logging, metrics, cache invalidation | Validation, authorization, exception handling |

---

## Complete Example: Request Lifecycle

```csharp
// 1. PreProcessor runs
public class CreateProductPreProcessor : IPreProcessor<CreateProductCommand, Result<int>>
{
    public Task Process(CreateProductCommand request, CancellationToken ct)
    {
        // Enrich, log, or validate before the handler
        return Task.CompletedTask;
    }
}

// 2. Handler runs
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<int>>
{
    public async Task<Result<int>> Handle(CreateProductCommand command, CancellationToken ct)
    {
        // Business logic
        return 42;
    }
}

// 3. PostProcessor runs
public class CreateProductPostProcessor : IPostProcessor<CreateProductCommand, Result<int>>
{
    public Task Process(CreateProductCommand request, Result<int> response, CancellationToken ct)
    {
        // Log, notify, or invalidate caches after the handler
        return Task.CompletedTask;
    }
}
```

The execution order for a full pipeline with behaviors and processors:

```
Behavior 1 (before)
  Behavior 2 (before)
    PreProcessors
      Handler
    PostProcessors
  Behavior 2 (after)
Behavior 1 (after)
```

---

## Next Steps

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Full behavior interface with short-circuit support
- **[Dependency Injection](12-dependency-injection.md)** — Explicit processor registration options
- **[Notifications](05-notifications.md)** — Processors applied to dispatch types
