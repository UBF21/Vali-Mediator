# Notifications

Notifications implement the publish-subscribe pattern: one notification can be handled by **multiple handlers**. They are ideal for domain events and cross-cutting reactions to business facts.

---

## INotification

Implement `INotification` to define a notification:

```csharp
using Vali_Mediator.Core.Notification;

public record OrderPlacedNotification(
    Guid OrderId,
    Guid CustomerId,
    decimal Total,
    string CustomerEmail) : INotification;

public record UserRegisteredNotification(
    Guid UserId,
    string Email,
    string Name) : INotification;

public record ProductStockLowNotification(
    int ProductId,
    string ProductName,
    int CurrentStock,
    int MinimumStock) : INotification;
```

`INotification` inherits from `IDispatch`, which enables pipeline behaviors for notifications (see [Pipeline Behaviors](08-pipeline-behaviors.md)).

---

## INotificationHandler

Each handler implements `INotificationHandler<TNotification>`:

```csharp
public class SendOrderConfirmationEmailHandler
    : INotificationHandler<OrderPlacedNotification>
{
    private readonly IEmailService _email;

    public SendOrderConfirmationEmailHandler(IEmailService email) => _email = email;

    public async Task Handle(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _email.SendOrderConfirmationAsync(
            notification.CustomerEmail,
            notification.OrderId,
            notification.Total,
            ct);
    }
}

public class UpdateInventoryHandler : INotificationHandler<OrderPlacedNotification>
{
    private readonly IInventoryService _inventory;

    public UpdateInventoryHandler(IInventoryService inventory) => _inventory = inventory;

    public async Task Handle(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _inventory.DeductStockForOrderAsync(notification.OrderId, ct);
    }
}

public class CreateInvoiceHandler : INotificationHandler<OrderPlacedNotification>
{
    private readonly IInvoiceService _invoices;

    public CreateInvoiceHandler(IInvoiceService invoices) => _invoices = invoices;

    public async Task Handle(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _invoices.CreateForOrderAsync(notification.OrderId, ct);
    }
}
```

All three handlers respond to `OrderPlacedNotification`. Vali-Mediator discovers and invokes all of them.

---

## Priority

`INotificationHandler<T>` exposes a `Priority` property (default: `0`). Handlers with **higher priority run first**. Handlers with the same priority run in registration order.

```csharp
// Priority 10 — runs first
public class AuditLogHandler : INotificationHandler<OrderPlacedNotification>
{
    public int Priority => 10;

    public async Task Handle(OrderPlacedNotification notification, CancellationToken ct)
    {
        // Log the event before any business side effects
        await _audit.LogAsync("ORDER_PLACED", notification.OrderId, ct);
    }
}

// Priority 5 — runs second
public class InventoryHandler : INotificationHandler<OrderPlacedNotification>
{
    public int Priority => 5;

    public async Task Handle(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _inventory.DeductStockForOrderAsync(notification.OrderId, ct);
    }
}

// Priority 0 (default) — runs last
public class EmailHandler : INotificationHandler<OrderPlacedNotification>
{
    // Priority defaults to 0
    public async Task Handle(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _email.SendConfirmationAsync(notification.CustomerEmail, ct);
    }
}
```

---

## Publishing Notifications

Use `IValiMediator.Publish` to dispatch a notification:

```csharp
// Default: Sequential strategy
await _mediator.Publish(new OrderPlacedNotification(order.Id, order.CustomerId, order.Total, email), ct);

// Explicit strategy
await _mediator.Publish(
    new OrderPlacedNotification(order.Id, order.CustomerId, order.Total, email),
    PublishStrategy.Parallel,
    ct);
```

---

## PublishStrategy

`PublishStrategy` controls how handlers are invoked when called with the overload that accepts a strategy:

| Strategy | Behavior |
|---|---|
| `Sequential` | Handlers run one after another, in descending `Priority` order. An exception in one handler stops subsequent handlers. This is the default when using the overload without a strategy. |
| `Parallel` | All handlers run concurrently via `Task.WhenAll`. All handlers run regardless of individual failures; all exceptions surface as `AggregateException`. |
| `ResilientParallel` | All handlers run concurrently. Individual failures are captured and the remaining handlers still execute. After all handlers complete, any exceptions are thrown as `AggregateException`. |

### Sequential (default)

```csharp
// Handlers run in Priority order: AuditLog → Inventory → Email
// If Inventory throws, Email is not called
await _mediator.Publish(new OrderPlacedNotification(...), ct);
```

### Parallel

```csharp
// All handlers start concurrently; Task.WhenAll waits for all
// If any handler throws, all exceptions are collected in AggregateException
await _mediator.Publish(
    new OrderPlacedNotification(...),
    PublishStrategy.Parallel,
    ct);
```

### ResilientParallel

```csharp
// All handlers run to completion even if one throws
// Exceptions from all failing handlers are collected in AggregateException
try
{
    await _mediator.Publish(
        new OrderPlacedNotification(...),
        PublishStrategy.ResilientParallel,
        ct);
}
catch (AggregateException ex)
{
    foreach (var inner in ex.InnerExceptions)
        _logger.LogError(inner, "Notification handler failed.");
}
```

Use `ResilientParallel` when handler failures should not prevent other handlers from completing (e.g., one email handler failing should not prevent the inventory handler from running).

---

## Practical Example: Order Placed Event

```csharp
// After successfully creating an order:
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    private readonly IOrderRepository _orders;
    private readonly IValiMediator _mediator;

    public CreateOrderCommandHandler(IOrderRepository orders, IValiMediator mediator)
    {
        _orders = orders;
        _mediator = mediator;
    }

    public async Task<Result<Guid>> Handle(CreateOrderCommand command, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Items);
        await _orders.SaveAsync(order, ct);

        // Publish to all interested handlers
        await _mediator.Publish(
            new OrderPlacedNotification(
                order.Id,
                order.CustomerId,
                order.Total,
                command.CustomerEmail),
            PublishStrategy.ResilientParallel,  // all handlers run even if one fails
            ct);

        return order.Id;
    }
}
```

---

## When to Use Notifications vs Requests

| Use | Mechanism |
|---|---|
| One handler, expects a response | `IRequest<TResponse>` |
| Multiple handlers, no response needed | `INotification` |
| One handler, no response, side effects | `IFireAndForget` |

---

## Next Steps

- **[Fire and Forget](06-fire-and-forget.md)** — One-way commands for background operations
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Apply behaviors to notifications via `IPipelineBehavior<TRequest>`
- **[Processors](09-processors.md)** — Pre/post processors for dispatch types
