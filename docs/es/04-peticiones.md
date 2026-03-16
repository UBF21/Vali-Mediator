# Peticiones

Las peticiones son el mecanismo de comunicación principal en Vali-Mediator. Una petición se envía a exactamente **un** handler y devuelve una respuesta.

---

## IRequest\<TResponse\>

Implementa `IRequest<TResponse>` para cualquier comando o consulta que devuelva un valor:

```csharp
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;

// Consulta: devuelve datos
public record GetOrderQuery(Guid OrderId) : IRequest<Result<OrderDto>>;

// Comando: devuelve el ID del recurso creado
public record CreateProductCommand(string Name, decimal Price, int Stock)
    : IRequest<Result<int>>;

// Comando: devuelve un resultado tipado
public record UpdateUserEmailCommand(Guid UserId, string NewEmail)
    : IRequest<Result<bool>>;
```

---

## IRequest (void)

Para comandos que no devuelven valor, implementa `IRequest` (no genérico). Es un atajo para `IRequest<Unit>`:

```csharp
// Comando sin valor de retorno
public record DeleteOrderCommand(Guid OrderId) : IRequest;

public record SendWelcomeEmailCommand(string Email, string Name) : IRequest;
```

También puedes usar `IRequest<Result>` (el struct `Result` no genérico) para comandos void que pueden fallar:

```csharp
public record ArchiveProductCommand(int ProductId) : IRequest<Result>;
```

---

## IRequestHandler

### Handler para una respuesta tipada

```csharp
public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, Result<OrderDto>>
{
    private readonly IOrderRepository _orders;

    public GetOrderQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(query.OrderId, ct);

        if (order is null)
            return Result<OrderDto>.Fail("Pedido no encontrado.", ErrorType.NotFound);

        return new OrderDto(order.Id, order.CustomerId, order.Total, order.Status);
    }
}
```

### Handler para una petición void

Cuando usas `IRequest` (no genérico), implementa `IRequestHandler<TRequest>` (un solo parámetro de tipo):

```csharp
// IRequest es atajo para IRequest<Unit>
// IRequestHandler<TRequest> es atajo para IRequestHandler<TRequest, Unit>
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

### Handler para un comando void que devuelve Result

```csharp
public class ArchiveProductCommandHandler : IRequestHandler<ArchiveProductCommand, Result>
{
    private readonly IProductRepository _products;

    public ArchiveProductCommandHandler(IProductRepository products) => _products = products;

    public async Task<Result> Handle(ArchiveProductCommand command, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(command.ProductId, ct);

        if (product is null)
            return Result.Fail("Producto no encontrado.", ErrorType.NotFound);

        if (product.IsArchived)
            return Result.Fail("El producto ya está archivado.", ErrorType.Conflict);

        await _products.ArchiveAsync(product.Id, ct);
        return Result.Ok();
    }
}
```

---

## Enviando Peticiones

Usa `IValiMediator.Send` para despachar una petición:

```csharp
// Devuelve Result<OrderDto> — lanza HandlerNotFoundException si no hay handler registrado
var result = await _mediator.Send(new GetOrderQuery(orderId), cancellationToken);

// Devuelve Unit — para handlers void
await _mediator.Send(new DeleteOrderCommand(orderId), cancellationToken);
```

---

## SendOrDefault

`SendOrDefault` devuelve `default(TResponse)` en lugar de lanzar `HandlerNotFoundException` cuando no hay handler registrado. Útil para características opcionales o handlers controlados por feature flags:

```csharp
// Devuelve null si no hay handler registrado, en lugar de lanzar excepción
var result = await _mediator.SendOrDefault(new GetProductCacheQuery(productId), ct);

if (result is null)
{
    // Sin handler de caché registrado — continuar hacia la base de datos
    result = await _mediator.Send(new GetProductQuery(productId), ct);
}
```

> **Nota:** `SendOrDefault` sigue lanzando excepciones para todos los demás errores (por ejemplo, errores en tiempo de ejecución del handler). Solo suprime `HandlerNotFoundException`.

---

## Una Petición, Un Handler

Vali-Mediator aplica un único handler por tipo de petición. Si registras dos handlers para el mismo `IRequest<TResponse>`, el último en registrarse gana (comportamiento estándar de DI). Esto es por diseño — para enviar a múltiples destinatarios, usa [Notificaciones](05-notificaciones.md).

---

## Patrones Prácticos

### CQRS — Separar Comandos y Consultas

```csharp
// Consultas: devuelven datos, sin efectos secundarios
public record GetProductQuery(int Id) : IRequest<Result<ProductDto>>;
public record ListProductsQuery(string? Category, int Page) : IRequest<Result<PagedList<ProductDto>>>;
public record SearchProductsQuery(string Term) : IRequest<Result<IReadOnlyList<ProductDto>>>;

// Comandos: mutan el estado, devuelven un ID o un resultado tipado
public record CreateProductCommand(string Name, decimal Price) : IRequest<Result<int>>;
public record UpdateProductCommand(int Id, string Name, decimal Price) : IRequest<Result>;
public record DeleteProductCommand(int Id) : IRequest<Result>;
```

### Devolviendo Errores de Validación

Al usar Vali-Validation con Vali-Mediator, los fallos de validación pueden exponerse como errores estructurados:

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

## Siguientes Pasos

- **[Result](10-resultado.md)** — Cómo usar `Result<T>` y `Result` de manera efectiva
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Aplicar concerns transversales a todos los handlers
- **[Notificaciones](05-notificaciones.md)** — Fan-out a múltiples handlers
- **[Fire and Forget](06-fire-and-forget.md)** — Comandos unidireccionales para efectos secundarios
