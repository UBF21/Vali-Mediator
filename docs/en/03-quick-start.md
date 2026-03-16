# Quick Start

This guide builds a complete request/handler example from scratch in about 5 minutes.

---

## Goal

Create a `GetProductQuery` that retrieves a product by ID and returns a `Result<ProductDto>`.

---

## Step 1 — Install the Package

```sh
dotnet add package Vali-Mediator
```

---

## Step 2 — Define the Request and Response

```csharp
// GetProductQuery.cs
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;

public record GetProductQuery(int ProductId) : IRequest<Result<ProductDto>>;

public record ProductDto(int Id, string Name, decimal Price, int Stock);
```

`IRequest<TResponse>` marks this class as a request that expects a `Result<ProductDto>` back.

---

## Step 3 — Implement the Handler

```csharp
// GetProductQueryHandler.cs
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;

public class GetProductQueryHandler : IRequestHandler<GetProductQuery, Result<ProductDto>>
{
    private readonly IProductRepository _products;

    public GetProductQueryHandler(IProductRepository products)
    {
        _products = products;
    }

    public async Task<Result<ProductDto>> Handle(GetProductQuery query, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(query.ProductId, ct);

        if (product is null)
            return Result<ProductDto>.Fail($"Product {query.ProductId} not found.", ErrorType.NotFound);

        return new ProductDto(product.Id, product.Name, product.Price, product.Stock);
        // Implicit conversion: ProductDto → Result<ProductDto>.Ok(...)
    }
}
```

Key points:
- The handler implements `IRequestHandler<GetProductQuery, Result<ProductDto>>`
- On failure, return `Result<ProductDto>.Fail(...)` with an appropriate `ErrorType`
- On success, return the value directly — the implicit operator wraps it in `Result<T>.Ok(value)`

---

## Step 4 — Register with DI

```csharp
// Program.cs
using Vali_Mediator.Core.General.Extension;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IProductRepository, ProductRepository>();

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssembly(typeof(Program).Assembly);
});

var app = builder.Build();
```

`RegisterServicesFromAssembly` scans the assembly and automatically registers `GetProductQueryHandler` for the `GetProductQuery` request.

---

## Step 5 — Use It in an Endpoint

### Minimal API

```csharp
app.MapGet("/products/{id:int}", async (int id, IValiMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(new GetProductQuery(id), ct);

    return result.Match(
        onSuccess: product => Results.Ok(product),
        onFailure: (error, errorType) => errorType switch
        {
            ErrorType.NotFound => Results.NotFound(new { error }),
            _ => Results.Problem(error)
        });
});
```

### Controller

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IValiMediator _mediator;

    public ProductsController(IValiMediator mediator) => _mediator = mediator;

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProductQuery(id), ct);

        return result.Match(
            onSuccess: product => Ok(product),
            onFailure: (error, errorType) => errorType switch
            {
                ErrorType.NotFound => NotFound(new { error }),
                _ => Problem(error)
            });
    }
}
```

---

## Step 6 — Run and Test

With the endpoint registered, call it:

```sh
curl http://localhost:5000/products/42
```

Success response:
```json
{
  "id": 42,
  "name": "Wireless Headphones",
  "price": 149.99,
  "stock": 23
}
```

Not found response:
```json
{
  "error": "Product 42 not found."
}
```

---

## What You Have Built

```
Request:  GetProductQuery(ProductId)
                │
        IValiMediator.Send(...)
                │
        GetProductQueryHandler.Handle(...)
                │
        Result<ProductDto>
                │
        Endpoint → HTTP response
```

- No shared state between request and handler
- No exceptions for "not found" — the failure is explicit and type-safe
- The handler only knows about the repository — not about HTTP, controllers, or DI

---

## Next Steps

- **[Requests](04-requests.md)** — Void requests, `SendOrDefault`, and more patterns
- **[Result](10-result.md)** — Map, Bind, Tap, Match, and validation errors
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Add logging or validation around every handler
- **[Notifications](05-notifications.md)** — Publish events to multiple handlers
