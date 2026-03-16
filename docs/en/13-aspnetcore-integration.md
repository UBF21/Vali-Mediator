# ASP.NET Core Integration

This article covers patterns for integrating Vali-Mediator with ASP.NET Core — mapping `Result<T>` to HTTP responses in controllers and Minimal APIs.

> **Note:** Vali-Mediator does not ship a separate ASP.NET Core package. The integration patterns shown here are idiomatic C# using the standard ASP.NET Core APIs. No additional NuGet package is required beyond `Vali-Mediator`.

---

## ErrorType → HTTP Status Code Mapping

The `ErrorType` enum maps naturally to HTTP status codes:

| ErrorType | HTTP Status | ASP.NET Core |
|---|---|---|
| `None` | — | (success, not an error) |
| `Validation` | 422 Unprocessable Entity | `Results.UnprocessableEntity` / `UnprocessableEntity` |
| `NotFound` | 404 Not Found | `Results.NotFound` / `NotFound` |
| `Conflict` | 409 Conflict | `Results.Conflict` / `Conflict` |
| `Unauthorized` | 401 Unauthorized | `Results.Unauthorized` / `Unauthorized` |
| `Forbidden` | 403 Forbidden | `Results.Forbid` / `Forbid` |
| `Failure` | 500 Internal Server Error | `Results.Problem` / `Problem` |

---

## Extension Methods (Recommended Pattern)

Define extension methods to keep controllers and endpoints clean:

### For Minimal APIs

```csharp
// ResultExtensions.cs
using Vali_Mediator.Core.Result;

public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        return result.Match(
            onSuccess: value => Results.Ok(value),
            onFailure: (error, errorType) => errorType switch
            {
                ErrorType.Validation   => Results.UnprocessableEntity(new
                {
                    type = "ValidationError",
                    errors = result.ValidationErrors ?? new Dictionary<string, IReadOnlyList<string>>
                        { ["general"] = new[] { error } }
                }),
                ErrorType.NotFound     => Results.NotFound(new { error }),
                ErrorType.Conflict     => Results.Conflict(new { error }),
                ErrorType.Unauthorized => Results.Unauthorized(),
                ErrorType.Forbidden    => Results.Forbid(),
                _                      => Results.Problem(
                    detail: error,
                    statusCode: StatusCodes.Status500InternalServerError)
            });
    }

    public static IResult ToHttpResult(this Result result)
    {
        return result.Match(
            onSuccess: () => Results.Ok(),
            onFailure: (error, errorType) => errorType switch
            {
                ErrorType.NotFound     => Results.NotFound(new { error }),
                ErrorType.Conflict     => Results.Conflict(new { error }),
                ErrorType.Unauthorized => Results.Unauthorized(),
                ErrorType.Forbidden    => Results.Forbid(),
                _                      => Results.Problem(detail: error)
            });
    }
}
```

### For Controllers

```csharp
// ControllerResultExtensions.cs
using Microsoft.AspNetCore.Mvc;
using Vali_Mediator.Core.Result;

public static class ControllerResultExtensions
{
    public static IActionResult ToActionResult<T>(
        this Result<T> result, ControllerBase controller)
    {
        return result.Match(
            onSuccess: value => (IActionResult)controller.Ok(value),
            onFailure: (error, errorType) => errorType switch
            {
                ErrorType.Validation   => controller.UnprocessableEntity(new
                {
                    type = "ValidationError",
                    errors = result.ValidationErrors
                }),
                ErrorType.NotFound     => controller.NotFound(new { error }),
                ErrorType.Conflict     => controller.Conflict(new { error }),
                ErrorType.Unauthorized => controller.Unauthorized(),
                ErrorType.Forbidden    => controller.Forbid(),
                _                      => controller.Problem(detail: error)
            });
    }

    public static IActionResult ToActionResult(
        this Result result, ControllerBase controller)
    {
        return result.Match(
            onSuccess: () => (IActionResult)controller.Ok(),
            onFailure: (error, errorType) => errorType switch
            {
                ErrorType.NotFound     => controller.NotFound(new { error }),
                ErrorType.Conflict     => controller.Conflict(new { error }),
                ErrorType.Unauthorized => controller.Unauthorized(),
                ErrorType.Forbidden    => controller.Forbid(),
                _                      => controller.Problem(detail: error)
            });
    }
}
```

---

## Minimal API Examples

### CRUD Endpoints

```csharp
var products = app.MapGroup("/api/products").WithTags("Products");

products.MapGet("/{id:int}", async (
    int id,
    IValiMediator mediator,
    CancellationToken ct) =>
{
    var result = await mediator.Send(new GetProductQuery(id), ct);
    return result.ToHttpResult();
});

products.MapPost("/", async (
    CreateProductRequest request,
    IValiMediator mediator,
    CancellationToken ct) =>
{
    var result = await mediator.Send(
        new CreateProductCommand(request.Name, request.Price, request.Stock), ct);

    return result.Match(
        onSuccess: id => Results.CreatedAtRoute("GetProduct", new { id }, new { id }),
        onFailure: (error, errorType) => errorType switch
        {
            ErrorType.Validation => Results.UnprocessableEntity(new { errors = result.ValidationErrors }),
            ErrorType.Conflict   => Results.Conflict(new { error }),
            _                    => Results.Problem(error)
        });
});

products.MapPut("/{id:int}", async (
    int id,
    UpdateProductRequest request,
    IValiMediator mediator,
    CancellationToken ct) =>
{
    var result = await mediator.Send(
        new UpdateProductCommand(id, request.Name, request.Price), ct);
    return result.ToHttpResult();
});

products.MapDelete("/{id:int}", async (
    int id,
    IValiMediator mediator,
    CancellationToken ct) =>
{
    var result = await mediator.Send(new DeleteProductCommand(id), ct);
    return result.ToHttpResult();
});
```

### Streaming Endpoint

```csharp
products.MapGet("/export", (
    IValiMediator mediator,
    CancellationToken ct) =>
{
    // ASP.NET Core serializes IAsyncEnumerable as a streaming JSON array
    return mediator.CreateStream(new ExportProductCatalogRequest(), ct);
});
```

---

## Controller Examples

### Products Controller

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IValiMediator _mediator;

    public ProductsController(IValiMediator mediator) => _mediator = mediator;

    [HttpGet("{id:int}", Name = "GetProduct")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProductQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpPost]
    [ProducesResponseType(typeof(int), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateProductCommand(request.Name, request.Price, request.Stock), ct);

        return result.Match(
            onSuccess: id => CreatedAtAction(nameof(Get), new { id }, new { id }),
            onFailure: (error, errorType) => errorType switch
            {
                ErrorType.Validation => UnprocessableEntity(new
                {
                    type = "ValidationError",
                    errors = result.ValidationErrors
                }),
                ErrorType.Conflict => Conflict(new { error }),
                _ => Problem(detail: error)
            });
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id, UpdateProductRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateProductCommand(id, request.Name, request.Price), ct);
        return result.ToActionResult(this);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteProductCommand(id), ct);
        return result.ToActionResult(this);
    }
}
```

---

## Handling ValidationErrors in Responses

When a handler returns a validation failure, the `ValidationErrors` dictionary can be forwarded directly to the client:

```csharp
// Handler
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
    // ...
}

// Endpoint response for ErrorType.Validation:
// HTTP 422
// {
//   "type": "ValidationError",
//   "errors": {
//     "Name": ["Name is required.", "Name cannot exceed 200 characters."],
//     "Price": ["Price must be greater than 0."]
//   }
// }
```

---

## Global Exception Handling

Add a global exception handler to catch unexpected errors:

```csharp
// Program.cs
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var error = feature?.Error;

        await context.Response.WriteAsJsonAsync(new
        {
            error = "An unexpected error occurred.",
            traceId = Activity.Current?.Id ?? context.TraceIdentifier
        });
    });
});
```

---

## Program.cs: Complete Setup

```csharp
using Vali_Mediator.Core.General.Extension;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IProductRepository, ProductRepository>();

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
});

var app = builder.Build();

app.UseExceptionHandler("/error");
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthorization();
app.MapControllers();

// Minimal API endpoints
var products = app.MapGroup("/api/products");
products.MapGet("/{id:int}", async (int id, IValiMediator m, CancellationToken ct)
    => (await m.Send(new GetProductQuery(id), ct)).ToHttpResult());

app.Run();
```

---

## Next Steps

- **[Result](10-result.md)** — Full reference for `Result<T>` and `Result`
- **[Requests](04-requests.md)** — Build handlers that return typed results
- **[Dependency Injection](12-dependency-injection.md)** — Complete registration reference
