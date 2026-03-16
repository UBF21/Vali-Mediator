# Inicio Rápido

Esta guía construye un ejemplo completo de petición/handler desde cero en aproximadamente 5 minutos.

---

## Objetivo

Crear una `GetProductQuery` que recupere un producto por ID y devuelva un `Result<ProductDto>`.

---

## Paso 1 — Instalar el Paquete

```sh
dotnet add package Vali-Mediator
```

---

## Paso 2 — Definir la Petición y la Respuesta

```csharp
// GetProductQuery.cs
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;

public record GetProductQuery(int ProductId) : IRequest<Result<ProductDto>>;

public record ProductDto(int Id, string Name, decimal Price, int Stock);
```

`IRequest<TResponse>` marca esta clase como una petición que espera un `Result<ProductDto>` de vuelta.

---

## Paso 3 — Implementar el Handler

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
            return Result<ProductDto>.Fail($"Producto {query.ProductId} no encontrado.", ErrorType.NotFound);

        return new ProductDto(product.Id, product.Name, product.Price, product.Stock);
        // Conversión implícita: ProductDto → Result<ProductDto>.Ok(...)
    }
}
```

Puntos clave:
- El handler implementa `IRequestHandler<GetProductQuery, Result<ProductDto>>`
- En caso de fallo, devuelve `Result<ProductDto>.Fail(...)` con el `ErrorType` apropiado
- En caso de éxito, devuelve el valor directamente — el operador implícito lo envuelve en `Result<T>.Ok(value)`

---

## Paso 4 — Registrar en DI

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

`RegisterServicesFromAssembly` escanea el assembly y registra automáticamente `GetProductQueryHandler` para la petición `GetProductQuery`.

---

## Paso 5 — Usarlo en un Endpoint

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

## Paso 6 — Ejecutar y Probar

Con el endpoint registrado, realiza una llamada:

```sh
curl http://localhost:5000/products/42
```

Respuesta exitosa:
```json
{
  "id": 42,
  "name": "Auriculares Inalámbricos",
  "price": 149.99,
  "stock": 23
}
```

Respuesta cuando no se encuentra:
```json
{
  "error": "Producto 42 no encontrado."
}
```

---

## Lo que Construiste

```
Petición: GetProductQuery(ProductId)
                │
        IValiMediator.Send(...)
                │
        GetProductQueryHandler.Handle(...)
                │
        Result<ProductDto>
                │
        Endpoint → Respuesta HTTP
```

- Sin estado compartido entre la petición y el handler
- Sin excepciones para "no encontrado" — el fallo es explícito y con tipo seguro
- El handler solo conoce el repositorio — no sabe nada de HTTP, controllers o DI

---

## Siguientes Pasos

- **[Peticiones](04-peticiones.md)** — Peticiones void, `SendOrDefault` y más patrones
- **[Result](10-resultado.md)** — Map, Bind, Tap, Match y errores de validación
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Agrega logging o validación alrededor de cada handler
- **[Notificaciones](05-notificaciones.md)** — Publica eventos a múltiples handlers
