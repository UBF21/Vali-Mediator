# Integración con ASP.NET Core

Este artículo cubre los patrones para integrar Vali-Mediator con ASP.NET Core — mapear `Result<T>` a respuestas HTTP en controllers y Minimal APIs.

> **Nota:** Vali-Mediator no incluye un paquete ASP.NET Core separado. Los patrones de integración que se muestran aquí son C# idiomático usando las APIs estándar de ASP.NET Core. No se requiere ningún paquete NuGet adicional más allá de `Vali-Mediator`.

---

## Mapeo ErrorType → Código de Estado HTTP

El enum `ErrorType` se mapea naturalmente a códigos de estado HTTP:

| ErrorType | Estado HTTP | ASP.NET Core |
|---|---|---|
| `None` | — | (éxito, no es un error) |
| `Validation` | 422 Unprocessable Entity | `Results.UnprocessableEntity` / `UnprocessableEntity` |
| `NotFound` | 404 Not Found | `Results.NotFound` / `NotFound` |
| `Conflict` | 409 Conflict | `Results.Conflict` / `Conflict` |
| `Unauthorized` | 401 Unauthorized | `Results.Unauthorized` / `Unauthorized` |
| `Forbidden` | 403 Forbidden | `Results.Forbid` / `Forbid` |
| `Failure` | 500 Internal Server Error | `Results.Problem` / `Problem` |

---

## Métodos de Extensión (Patrón Recomendado)

Define métodos de extensión para mantener los controllers y endpoints limpios:

### Para Minimal APIs

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

### Para Controllers

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

## Ejemplos con Minimal API

### Endpoints CRUD

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

### Endpoint de Streaming

```csharp
products.MapGet("/export", (
    IValiMediator mediator,
    CancellationToken ct) =>
{
    // ASP.NET Core serializa IAsyncEnumerable como un array JSON en streaming
    return mediator.CreateStream(new ExportProductCatalogRequest(), ct);
});
```

---

## Ejemplos con Controller

### Controller de Productos

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

## Manejo de ValidationErrors en Respuestas

Cuando un handler devuelve un fallo de validación, el diccionario `ValidationErrors` puede enviarse directamente al cliente:

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

// Respuesta del endpoint para ErrorType.Validation:
// HTTP 422
// {
//   "type": "ValidationError",
//   "errors": {
//     "Name": ["El nombre es obligatorio.", "El nombre no puede exceder 200 caracteres."],
//     "Price": ["El precio debe ser mayor que 0."]
//   }
// }
```

---

## Manejo Global de Excepciones

Agrega un manejador global de excepciones para capturar errores inesperados:

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
            error = "Ocurrió un error inesperado.",
            traceId = Activity.Current?.Id ?? context.TraceIdentifier
        });
    });
});
```

---

## Program.cs: Configuración Completa

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

// Endpoints de Minimal API
var products = app.MapGroup("/api/products");
products.MapGet("/{id:int}", async (int id, IValiMediator m, CancellationToken ct)
    => (await m.Send(new GetProductQuery(id), ct)).ToHttpResult());

app.Run();
```

---

## Siguientes Pasos

- **[Result](10-resultado.md)** — Referencia completa de `Result<T>` y `Result`
- **[Peticiones](04-peticiones.md)** — Construir handlers que devuelven resultados tipados
- **[Inyección de Dependencias](12-inyeccion-dependencias.md)** — Referencia completa de registro
