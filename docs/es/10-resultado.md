# Result

Vali-Mediator incluye dos tipos result integrados que proporcionan una forma tipada y sin excepciones de representar los resultados de una operación:

- `Result<T>` — para operaciones que devuelven un valor en caso de éxito
- `Result` — para operaciones void que pueden fallar

---

## Enum ErrorType

Todos los fallos de resultado llevan un `ErrorType` que clasifica semánticamente el fallo:

```csharp
public enum ErrorType
{
    None = 0,           // Sin error — el resultado es exitoso
    Validation = 1,     // Los datos de entrada fallaron las reglas de validación
    NotFound = 2,       // El recurso solicitado no existe
    Conflict = 3,       // La operación conflictúa con el estado actual (por ejemplo, duplicado)
    Unauthorized = 4,   // El llamante no está autenticado
    Forbidden = 5,      // El llamante no tiene permiso
    Failure = 6         // Fallo general no clasificado
}
```

Usa `ErrorType` para mapear fallos a códigos de estado HTTP, nivel de severidad de logging o mensajes de UI.

---

## Result\<T\>

`Result<T>` es un `readonly struct` — un tipo valor que no puede ser null.

### Propiedades

| Propiedad | Tipo | Descripción |
|---|---|---|
| `IsSuccess` | `bool` | `true` cuando la operación fue exitosa |
| `IsFailure` | `bool` | `true` cuando la operación falló |
| `Value` | `T?` | El valor de éxito. Solo válido cuando `IsSuccess` es `true`. |
| `Error` | `string?` | Descripción del error legible. Solo válida cuando `IsFailure` es `true`. |
| `ErrorType` | `ErrorType` | Categoría semántica del fallo. `ErrorType.None` en caso de éxito. |
| `ValidationErrors` | `IReadOnlyDictionary<string, IReadOnlyList<string>>?` | Solo se rellena para fallos con `ErrorType.Validation`. |

### Métodos de Fábrica

```csharp
// Éxito
var success = Result<int>.Ok(42);

// Fallo con mensaje y tipo
var notFound = Result<OrderDto>.Fail("Pedido no encontrado.", ErrorType.NotFound);
var conflict = Result<UserDto>.Fail("El email ya está en uso.", ErrorType.Conflict);

// Fallo con errores de validación estructurados
var validationFail = Result<int>.Fail(
    new Dictionary<string, List<string>>
    {
        ["Name"] = new() { "El nombre es obligatorio.", "El nombre no puede exceder 200 caracteres." },
        ["Price"] = new() { "El precio debe ser mayor que 0." }
    },
    ErrorType.Validation);
```

### Conversión Implícita

`Result<T>` tiene una conversión implícita desde `T`. Esto permite que los handlers devuelvan valores directamente sin escribir `Result<T>.Ok(value)`:

```csharp
public async Task<Result<ProductDto>> Handle(GetProductQuery query, CancellationToken ct)
{
    var product = await _products.GetByIdAsync(query.ProductId, ct);

    if (product is null)
        return Result<ProductDto>.Fail("Producto no encontrado.", ErrorType.NotFound);

    // Implícito: ProductDto → Result<ProductDto>.Ok(dto)
    return new ProductDto(product.Id, product.Name, product.Price);
}
```

---

## Result (no genérico)

`Result` es el equivalente void. Úsalo cuando un handler realiza una acción pero no devuelve un valor:

```csharp
// Éxito
var ok = Result.Ok();

// Fallo
var fail = Result.Fail("Usuario no encontrado.", ErrorType.NotFound);
```

```csharp
public async Task<Result> Handle(ArchiveProductCommand command, CancellationToken ct)
{
    var product = await _products.GetByIdAsync(command.ProductId, ct);
    if (product is null) return Result.Fail("Producto no encontrado.", ErrorType.NotFound);
    if (product.IsArchived) return Result.Fail("Ya está archivado.", ErrorType.Conflict);

    await _products.ArchiveAsync(product.Id, ct);
    return Result.Ok();
}
```

---

## Match

`Match` ejecuta una de dos funciones dependiendo del éxito o del fallo:

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

`Map` transforma el valor de éxito sin cambiar el tipo del result. Los fallos se propagan sin cambios:

```csharp
Result<Product> productResult = await _mediator.Send(new GetProductQuery(id), ct);

// Transforma Product → ProductDto solo si fue exitoso
Result<ProductDto> dtoResult = productResult.Map(p => new ProductDto(p.Id, p.Name, p.Price));

// Versión async
Result<ProductDto> dtoResult = await productResult.MapAsync(
    async p => await _mapper.MapAsync(p, ct));
```

---

## Bind

`Bind` encadena operaciones que devuelven `Result<T>`. Si el resultado inicial es un fallo, la cadena se detiene:

```csharp
// Sin Bind (sentencias if anidadas):
var productResult = await _mediator.Send(new GetProductQuery(id), ct);
if (productResult.IsFailure) return Result<OrderId>.Fail(productResult.Error!, productResult.ErrorType);

var priceResult = await _mediator.Send(new GetCurrentPriceQuery(id), ct);
if (priceResult.IsFailure) return Result<OrderId>.Fail(priceResult.Error!, priceResult.ErrorType);

// Con Bind (cadena lineal):
var orderId = await (await _mediator.Send(new GetProductQuery(id), ct))
    .BindAsync(product => _mediator.Send(new GetCurrentPriceQuery(product.Id), ct))
    .BindAsync(price => _mediator.Send(new CreateOrderCommand(id, price.Amount), ct));
```

```csharp
// Bind sincrónico
Result<ProductDto> result = GetProduct(id)
    .Bind(product => CheckAvailability(product))
    .Bind(available => BuildDto(available));
```

---

## Tap

`Tap` ejecuta un efecto secundario en caso de éxito sin cambiar el resultado:

```csharp
var result = await _mediator.Send(new CreateOrderCommand(request), ct);

result.Tap(order =>
{
    // Efecto secundario: registrar el nuevo ID de pedido
    _logger.LogInformation("Pedido {OrderId} creado.", order.Id);
});

// Encadenar: devuelve el mismo resultado sin cambios
return result
    .Tap(order => _cache.Set($"order:{order.Id}", order))
    .Tap(order => _metrics.Increment("orders.created"));
```

---

## OnFailure

`OnFailure` ejecuta un efecto secundario cuando el resultado es un fallo:

```csharp
var result = await _mediator.Send(new CreateOrderCommand(request), ct);

result.OnFailure((error, errorType) =>
{
    _logger.LogWarning("Fallo al crear pedido: {Error} ({ErrorType}).", error, errorType);
});

// Encadenar varios:
return result
    .Tap(order => _metrics.Increment("orders.created"))
    .OnFailure((error, type) => _metrics.Increment("orders.failed"));
```

---

## ValidationErrors

Cuando un resultado se crea con errores de validación estructurados, accede a ellos vía `ValidationErrors`:

```csharp
var result = await _mediator.Send(new CreateProductCommand(name, price), ct);

if (result.IsFailure && result.ErrorType == ErrorType.Validation)
{
    // ValidationErrors es IReadOnlyDictionary<string, IReadOnlyList<string>>
    foreach (var (field, errors) in result.ValidationErrors!)
    {
        foreach (var error in errors)
            Console.WriteLine($"{field}: {error}");
    }
}
```

---

## Ejemplo Completo de Handler

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
        // Validar que cada producto existe
        foreach (var item in command.Items)
        {
            var product = await _products.GetByIdAsync(item.ProductId, ct);
            if (product is null)
                return Result<OrderDto>.Fail(
                    $"Producto {item.ProductId} no encontrado.", ErrorType.NotFound);

            if (product.Stock < item.Quantity)
                return Result<OrderDto>.Fail(
                    $"Stock insuficiente para el producto {item.ProductId}.", ErrorType.Conflict);
        }

        var order = Order.Create(command.CustomerId, command.Items);
        await _orders.SaveAsync(order, ct);

        await _mediator.Publish(new OrderPlacedNotification(
            order.Id, command.CustomerId, order.Total, command.CustomerEmail), ct);

        // Conversión implícita: OrderDto → Result<OrderDto>.Ok(dto)
        return new OrderDto(order.Id, order.CustomerId, order.Total, order.Status);
    }
}
```

---

## Resumen

| Método | En éxito | En fallo |
|---|---|---|
| `Ok(value)` / `Ok()` | Crea resultado exitoso | N/A |
| `Fail(error, type)` | N/A | Crea fallo |
| `Fail(dict, type)` | N/A | Crea fallo de validación |
| `Match(onSuccess, onFailure)` | Llama a `onSuccess` | Llama a `onFailure` |
| `Map(mapper)` | Aplica mapper | Propaga fallo |
| `MapAsync(mapper)` | Aplica mapper async | Propaga fallo |
| `Bind(binder)` | Llama al binder | Propaga fallo |
| `BindAsync(binder)` | Llama al binder async | Propaga fallo |
| `Tap(action)` | Ejecuta action, devuelve mismo resultado | No hace nada |
| `OnFailure(action)` | No hace nada | Ejecuta action, devuelve mismo resultado |

---

## Siguientes Pasos

- **[Integración ASP.NET Core](13-integracion-aspnetcore.md)** — Mapear `ErrorType` a códigos de estado HTTP
- **[Peticiones](04-peticiones.md)** — Usar `Result<T>` en handlers de peticiones
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Interceptar y transformar resultados en behaviors
