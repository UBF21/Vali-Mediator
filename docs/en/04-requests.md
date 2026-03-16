# Requests

Requests are the primary communication mechanism in Vali-Mediator. A request is sent to exactly **one** handler and returns a response.

---

## IRequest\<TResponse\>

Implement `IRequest<TResponse>` for any command or query that returns a value:

```csharp
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;

// Query: returns data
public record GetOrderQuery(Guid OrderId) : IRequest<Result<OrderDto>>;

// Command: returns the created resource ID
public record CreateProductCommand(string Name, decimal Price, int Stock)
    : IRequest<Result<int>>;

// Command: returns a typed result
public record UpdateUserEmailCommand(Guid UserId, string NewEmail)
    : IRequest<Result<bool>>;
```

---

## IRequest (void)

For commands that do not return a value, implement `IRequest` (non-generic). This is shorthand for `IRequest<Unit>`:

```csharp
// Command with no return value
public record DeleteOrderCommand(Guid OrderId) : IRequest;

public record SendWelcomeEmailCommand(string Email, string Name) : IRequest;
```

You can also use `IRequest<Result>` (the non-generic `Result` struct) for void commands that may fail:

```csharp
public record ArchiveProductCommand(int ProductId) : IRequest<Result>;
```

---

## IRequestHandler

### Handler for a typed response

```csharp
public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, Result<OrderDto>>
{
    private readonly IOrderRepository _orders;

    public GetOrderQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(query.OrderId, ct);

        if (order is null)
            return Result<OrderDto>.Fail("Order not found.", ErrorType.NotFound);

        return new OrderDto(order.Id, order.CustomerId, order.Total, order.Status);
    }
}
```

### Handler for a void request

When using `IRequest` (non-generic), implement `IRequestHandler<TRequest>` (single type parameter):

```csharp
// IRequest is shorthand for IRequest<Unit>
// IRequestHandler<TRequest> is shorthand for IRequestHandler<TRequest, Unit>
public class DeleteOrderCommandHandler : IRequestHandler<DeleteOrderCommand>
{
    private readonly IOrderRepository _orders;

    public DeleteOrderCommandHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Unit> Handle(DeleteOrderCommand command, CancellationToken ct)
    {
        await _orders.DeleteAsync(command.OrderId, ct);
        return Unit.Value;
    }
}
```

### Handler for a Result-returning void command

```csharp
public class ArchiveProductCommandHandler : IRequestHandler<ArchiveProductCommand, Result>
{
    private readonly IProductRepository _products;

    public ArchiveProductCommandHandler(IProductRepository products) => _products = products;

    public async Task<Result> Handle(ArchiveProductCommand command, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(command.ProductId, ct);

        if (product is null)
            return Result.Fail("Product not found.", ErrorType.NotFound);

        if (product.IsArchived)
            return Result.Fail("Product is already archived.", ErrorType.Conflict);

        await _products.ArchiveAsync(product.Id, ct);
        return Result.Ok();
    }
}
```

---

## Sending Requests

Use `IValiMediator.Send` to dispatch a request:

```csharp
// Returns Result<OrderDto> — throws HandlerNotFoundException if no handler is registered
var result = await _mediator.Send(new GetOrderQuery(orderId), cancellationToken);

// Returns Unit — for void handlers
await _mediator.Send(new DeleteOrderCommand(orderId), cancellationToken);
```

---

## SendOrDefault

`SendOrDefault` returns `default(TResponse)` instead of throwing `HandlerNotFoundException` when no handler is registered. Useful for optional features or feature-flag-driven handlers:

```csharp
// Returns null if no handler is registered, instead of throwing
var result = await _mediator.SendOrDefault(new GetProductCacheQuery(productId), ct);

if (result is null)
{
    // No cache handler registered — fall through to the database
    result = await _mediator.Send(new GetProductQuery(productId), ct);
}
```

> **Note:** `SendOrDefault` still throws for all other exceptions (e.g., handler runtime errors). It only suppresses `HandlerNotFoundException`.

---

## One Request, One Handler

Vali-Mediator enforces a single handler per request type. If you register two handlers for the same `IRequest<TResponse>`, the last-registered handler wins (standard DI behavior). This is by design — for fan-out to multiple recipients, use [Notifications](05-notifications.md).

---

## Practical Patterns

### CQRS — Separate Commands and Queries

```csharp
// Queries: return data, no side effects
public record GetProductQuery(int Id) : IRequest<Result<ProductDto>>;
public record ListProductsQuery(string? Category, int Page) : IRequest<Result<PagedList<ProductDto>>>;
public record SearchProductsQuery(string Term) : IRequest<Result<IReadOnlyList<ProductDto>>>;

// Commands: mutate state, return an ID or a typed result
public record CreateProductCommand(string Name, decimal Price) : IRequest<Result<int>>;
public record UpdateProductCommand(int Id, string Name, decimal Price) : IRequest<Result>;
public record DeleteProductCommand(int Id) : IRequest<Result>;
```

### Returning Validation Errors

When using Vali-Validation with Vali-Mediator, validation failures can be surfaced as structured errors:

```csharp
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<int>>
{
    private readonly IValidator<CreateProductCommand> _validator;
    private readonly IProductRepository _products;

    public CreateProductCommandHandler(
        IValidator<CreateProductCommand> validator,
        IProductRepository products)
    {
        _validator = validator;
        _products = products;
    }

    public async Task<Result<int>> Handle(CreateProductCommand command, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
        {
            return Result<int>.Fail(
                validation.Errors.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToList()),
                ErrorType.Validation);
        }

        var product = new Product(command.Name, command.Price);
        var id = await _products.CreateAsync(product, ct);
        return id;
    }
}
```

---

## Next Steps

- **[Result](10-result.md)** — How to use `Result<T>` and `Result` effectively
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Apply cross-cutting concerns to all handlers
- **[Notifications](05-notifications.md)** — Fan-out to multiple handlers
- **[Fire and Forget](06-fire-and-forget.md)** — One-way commands for side effects
