# Notificaciones

Las notificaciones implementan el patrón publish-subscribe: una notificación puede ser manejada por **múltiples handlers**. Son ideales para eventos de dominio y reacciones transversales a hechos de negocio.

---

## INotification

Implementa `INotification` para definir una notificación:

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

`INotification` hereda de `IDispatch`, lo que habilita los pipeline behaviors para notificaciones (ver [Pipeline Behaviors](08-pipeline-behaviors.md)).

---

## INotificationHandler

Cada handler implementa `INotificationHandler<TNotification>`:

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

Los tres handlers responden a `OrderPlacedNotification`. Vali-Mediator los descubre e invoca a todos.

---

## Priority

`INotificationHandler<T>` expone una propiedad `Priority` (valor por defecto: `0`). Los handlers con **mayor prioridad se ejecutan primero**. Los handlers con la misma prioridad se ejecutan en orden de registro.

```csharp
// Priority 10 — se ejecuta primero
public class AuditLogHandler : INotificationHandler<OrderPlacedNotification>
{
    public int Priority => 10;

    public async Task Handle(OrderPlacedNotification notification, CancellationToken ct)
    {
        // Registrar el evento antes de cualquier efecto secundario de negocio
        await _audit.LogAsync("ORDER_PLACED", notification.OrderId, ct);
    }
}

// Priority 5 — se ejecuta segundo
public class InventoryHandler : INotificationHandler<OrderPlacedNotification>
{
    public int Priority => 5;

    public async Task Handle(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _inventory.DeductStockForOrderAsync(notification.OrderId, ct);
    }
}

// Priority 0 (valor por defecto) — se ejecuta al final
public class EmailHandler : INotificationHandler<OrderPlacedNotification>
{
    // Priority tiene valor por defecto 0
    public async Task Handle(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _email.SendConfirmationAsync(notification.CustomerEmail, ct);
    }
}
```

---

## Publicando Notificaciones

Usa `IValiMediator.Publish` para despachar una notificación:

```csharp
// Por defecto: estrategia Sequential
await _mediator.Publish(new OrderPlacedNotification(order.Id, order.CustomerId, order.Total, email), ct);

// Estrategia explícita
await _mediator.Publish(
    new OrderPlacedNotification(order.Id, order.CustomerId, order.Total, email),
    PublishStrategy.Parallel,
    ct);
```

---

## PublishStrategy

`PublishStrategy` controla cómo se invocan los handlers cuando se llama con la sobrecarga que acepta una estrategia:

| Estrategia | Comportamiento |
|---|---|
| `Sequential` | Los handlers se ejecutan uno tras otro, en orden descendente de `Priority`. Una excepción en un handler detiene los handlers siguientes. Es el comportamiento por defecto cuando se usa la sobrecarga sin estrategia. |
| `Parallel` | Todos los handlers se ejecutan concurrentemente vía `Task.WhenAll`. Todos los handlers se ejecutan independientemente de fallos individuales; todas las excepciones se exponen como `AggregateException`. |
| `ResilientParallel` | Todos los handlers se ejecutan concurrentemente. Los fallos individuales se capturan y los handlers restantes siguen ejecutándose. Cuando todos completan, las excepciones se lanzan como `AggregateException`. |

### Sequential (por defecto)

```csharp
// Los handlers se ejecutan en orden de Priority: AuditLog → Inventory → Email
// Si Inventory lanza excepción, Email no se llama
await _mediator.Publish(new OrderPlacedNotification(...), ct);
```

### Parallel

```csharp
// Todos los handlers inician concurrentemente; Task.WhenAll espera a todos
// Si algún handler lanza, todas las excepciones se recopilan en AggregateException
await _mediator.Publish(
    new OrderPlacedNotification(...),
    PublishStrategy.Parallel,
    ct);
```

### ResilientParallel

```csharp
// Todos los handlers se ejecutan hasta completar aunque uno lance excepción
// Las excepciones de todos los handlers fallidos se recopilan en AggregateException
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
        _logger.LogError(inner, "Un handler de notificación falló.");
}
```

Usa `ResilientParallel` cuando el fallo de un handler no debe impedir que los demás handlers completen su trabajo (por ejemplo, si falla el handler de email no debe impedir que el handler de inventario se ejecute).

---

## Ejemplo Práctico: Evento Order Placed

```csharp
// Después de crear un pedido exitosamente:
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

        // Publicar a todos los handlers interesados
        await _mediator.Publish(
            new OrderPlacedNotification(
                order.Id,
                order.CustomerId,
                order.Total,
                command.CustomerEmail),
            PublishStrategy.ResilientParallel,  // todos los handlers se ejecutan aunque uno falle
            ct);

        return order.Id;
    }
}
```

---

## Cuándo Usar Notificaciones vs Peticiones

| Usar | Mecanismo |
|---|---|
| Un handler, espera una respuesta | `IRequest<TResponse>` |
| Múltiples handlers, sin respuesta necesaria | `INotification` |
| Un handler, sin respuesta, efectos secundarios | `IFireAndForget` |

---

## Siguientes Pasos

- **[Fire and Forget](06-fire-and-forget.md)** — Comandos unidireccionales para operaciones en background
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Aplicar behaviors a notificaciones vía `IPipelineBehavior<TRequest>`
- **[Procesadores](09-procesadores.md)** — Pre/post processors para tipos dispatch
