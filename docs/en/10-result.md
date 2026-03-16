# Result

Vali-Mediator includes two built-in result types that provide a typed, exception-free way to represent operation outcomes:

- `Result<T>` — for operations that return a value on success
- `Result` — for void-returning operations that may fail

---

## ErrorType Enum

All result failures carry an `ErrorType` that classifies the failure semantically:

```csharp
public enum ErrorType
{
    None = 0,           // No error — result is successful
    Validation = 1,     // Input data failed validation rules
    NotFound = 2,       // Requested resource does not exist
    Conflict = 3,       // Operation conflicts with current state (e.g., duplicate)
    Unauthorized = 4,   // Caller is not authenticated
    Forbidden = 5,      // Caller lacks permission
    Failure = 6         // General, unclassified failure
}
```

Use `ErrorType` to map failures to HTTP status codes, log severity, or UI messages.

---

## Result\<T\>

`Result<T>` is a `readonly struct` — a value type that cannot be null.

### Properties

| Property | Type | Description |
|---|---|---|
| `IsSuccess` | `bool` | `true` when the operation succeeded |
| `IsFailure` | `bool` | `true` when the operation failed |
| `Value` | `T?` | The success value. Only valid when `IsSuccess` is `true`. |
| `Error` | `string?` | Human-readable error description. Only valid when `IsFailure` is `true`. |
| `ErrorType` | `ErrorType` | Semantic category of the failure. `ErrorType.None` on success. |
| `ValidationErrors` | `IReadOnlyDictionary<string, IReadOnlyList<string>>?` | Only populated for `ErrorType.Validation` failures. |

### Factory Methods

```csharp
// Success
var success = Result<int>.Ok(42);

// Failure with message and type
var notFound = Result<OrderDto>.Fail("Order not found.", ErrorType.NotFound);
var conflict = Result<UserDto>.Fail("Email already in use.", ErrorType.Conflict);

// Failure with structured validation errors
var validationFail = Result<int>.Fail(
    new Dictionary<string, List<string>>
    {
        ["Name"] = new() { "Name is required.", "Name cannot exceed 200 characters." },
        ["Price"] = new() { "Price must be greater than 0." }
    },
    ErrorType.Validation);
```

### Implicit Conversion

`Result<T>` has an implicit conversion from `T`. This lets handlers return values directly without writing `Result<T>.Ok(value)`:

```csharp
public async Task<Result<ProductDto>> Handle(GetProductQuery query, CancellationToken ct)
{
    var product = await _products.GetByIdAsync(query.ProductId, ct);

    if (product is null)
        return Result<ProductDto>.Fail("Product not found.", ErrorType.NotFound);

    // Implicit: ProductDto → Result<ProductDto>.Ok(dto)
    return new ProductDto(product.Id, product.Name, product.Price);
}
```

---

## Result (non-generic)

`Result` is the void counterpart. Use it when a handler performs an action but does not return a value:

```csharp
// Success
var ok = Result.Ok();

// Failure
var fail = Result.Fail("User not found.", ErrorType.NotFound);
```

```csharp
public async Task<Result> Handle(ArchiveProductCommand command, CancellationToken ct)
{
    var product = await _products.GetByIdAsync(command.ProductId, ct);
    if (product is null) return Result.Fail("Product not found.", ErrorType.NotFound);
    if (product.IsArchived) return Result.Fail("Already archived.", ErrorType.Conflict);

    await _products.ArchiveAsync(product.Id, ct);
    return Result.Ok();
}
```

---

## Match

`Match` executes one of two functions depending on success or failure:

```csharp
// Result<T>.Match
var httpResult = result.Match(
    onSuccess: product => Results.Ok(product),
    onFailure: (error, errorType) => errorType switch
    {
        ErrorType.NotFound    => Results.NotFound(new { error }),
        ErrorType.Conflict    => Results.Conflict(new { error }),
        ErrorType.Validation  => Results.UnprocessableEntity(new { error }),
        _                     => Results.Problem(error)
    });

// Result.Match
var actionResult = result.Match(
    onSuccess: () => Ok(),
    onFailure: (error, errorType) => errorType switch
    {
        ErrorType.NotFound => NotFound(error),
        _ => Problem(error)
    });
```

---

## Map

`Map` transforms the success value without changing the result type. Failures propagate unchanged:

```csharp
Result<Product> productResult = await _mediator.Send(new GetProductQuery(id), ct);

// Transform Product → ProductDto only if successful
Result<ProductDto> dtoResult = productResult.Map(p => new ProductDto(p.Id, p.Name, p.Price));

// Async version
Result<ProductDto> dtoResult = await productResult.MapAsync(
    async p => await _mapper.MapAsync(p, ct));
```

---

## Bind

`Bind` chains operations that return `Result<T>`. If the initial result is a failure, the chain stops:

```csharp
// Without Bind (nested if statements):
var productResult = await _mediator.Send(new GetProductQuery(id), ct);
if (productResult.IsFailure) return Result<OrderId>.Fail(productResult.Error!, productResult.ErrorType);

var priceResult = await _mediator.Send(new GetCurrentPriceQuery(id), ct);
if (priceResult.IsFailure) return Result<OrderId>.Fail(priceResult.Error!, priceResult.ErrorType);

// With Bind (linear chain):
var orderId = await (await _mediator.Send(new GetProductQuery(id), ct))
    .BindAsync(product => _mediator.Send(new GetCurrentPriceQuery(product.Id), ct))
    .BindAsync(price => _mediator.Send(new CreateOrderCommand(id, price.Amount), ct));
```

```csharp
// Synchronous Bind
Result<ProductDto> result = GetProduct(id)
    .Bind(product => CheckAvailability(product))
    .Bind(available => BuildDto(available));
```

---

## Tap

`Tap` executes a side effect on success without changing the result:

```csharp
var result = await _mediator.Send(new CreateOrderCommand(request), ct);

result.Tap(order =>
{
    // Side effect: log the new order ID
    _logger.LogInformation("Order {OrderId} created.", order.Id);
});

// Chain: returns the same result unchanged
return result
    .Tap(order => _cache.Set($"order:{order.Id}", order))
    .Tap(order => _metrics.Increment("orders.created"));
```

---

## OnFailure

`OnFailure` executes a side effect when the result is a failure:

```csharp
var result = await _mediator.Send(new CreateOrderCommand(request), ct);

result.OnFailure((error, errorType) =>
{
    _logger.LogWarning("Order creation failed: {Error} ({ErrorType}).", error, errorType);
});

// Chain multiple:
return result
    .Tap(order => _metrics.Increment("orders.created"))
    .OnFailure((error, type) => _metrics.Increment("orders.failed"));
```

---

## ValidationErrors

When a result is created with structured validation errors, access them via `ValidationErrors`:

```csharp
var result = await _mediator.Send(new CreateProductCommand(name, price), ct);

if (result.IsFailure && result.ErrorType == ErrorType.Validation)
{
    // ValidationErrors is IReadOnlyDictionary<string, IReadOnlyList<string>>
    foreach (var (field, errors) in result.ValidationErrors!)
    {
        foreach (var error in errors)
            Console.WriteLine($"{field}: {error}");
    }
}
```

---

## Complete Handler Example

```csharp
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<OrderDto>>
{
    private readonly IOrderRepository _orders;
    private readonly IProductRepository _products;
    private readonly IValiMediator _mediator;

    public CreateOrderCommandHandler(
        IOrderRepository orders,
        IProductRepository products,
        IValiMediator mediator)
    {
        _orders = orders;
        _products = products;
        _mediator = mediator;
    }

    public async Task<Result<OrderDto>> Handle(CreateOrderCommand command, CancellationToken ct)
    {
        // Validate each product exists
        foreach (var item in command.Items)
        {
            var product = await _products.GetByIdAsync(item.ProductId, ct);
            if (product is null)
                return Result<OrderDto>.Fail(
                    $"Product {item.ProductId} not found.", ErrorType.NotFound);

            if (product.Stock < item.Quantity)
                return Result<OrderDto>.Fail(
                    $"Insufficient stock for product {item.ProductId}.", ErrorType.Conflict);
        }

        var order = Order.Create(command.CustomerId, command.Items);
        await _orders.SaveAsync(order, ct);

        await _mediator.Publish(new OrderPlacedNotification(
            order.Id, command.CustomerId, order.Total, command.CustomerEmail), ct);

        // Implicit conversion: OrderDto → Result<OrderDto>.Ok(dto)
        return new OrderDto(order.Id, order.CustomerId, order.Total, order.Status);
    }
}
```

---

## Summary

| Method | On success | On failure |
|---|---|---|
| `Ok(value)` / `Ok()` | Creates success | N/A |
| `Fail(error, type)` | N/A | Creates failure |
| `Fail(dict, type)` | N/A | Creates validation failure |
| `Match(onSuccess, onFailure)` | Calls `onSuccess` | Calls `onFailure` |
| `Map(mapper)` | Applies mapper | Propagates failure |
| `MapAsync(mapper)` | Applies async mapper | Propagates failure |
| `Bind(binder)` | Calls binder | Propagates failure |
| `BindAsync(binder)` | Calls async binder | Propagates failure |
| `Tap(action)` | Executes action, returns same result | No-op |
| `OnFailure(action)` | No-op | Executes action, returns same result |

---

## Next Steps

- **[ASP.NET Core Integration](13-aspnetcore-integration.md)** — Map `ErrorType` to HTTP status codes
- **[Requests](04-requests.md)** — Use `Result<T>` in request handlers
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Intercept and transform results in behaviors
