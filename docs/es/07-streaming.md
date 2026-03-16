# Streaming

Las peticiones de streaming devuelven una secuencia asíncrona de elementos vía `IAsyncEnumerable<T>`. Están diseñadas para escenarios donde los resultados se producen y consumen de forma incremental — reportes, datasets grandes, feeds en tiempo real y server-sent events.

---

## IStreamRequest\<TResponse\>

Implementa `IStreamRequest<TResponse>` para definir una petición de streaming:

```csharp
using Vali_Mediator.Core.Streaming;

public record GetOrdersReportStreamRequest(
    DateOnly From,
    DateOnly To,
    string? CustomerId = null) : IStreamRequest<OrderReportLine>;

public record ExportProductCatalogRequest(
    string? Category = null,
    bool IncludeInactive = false) : IStreamRequest<ProductExportRow>;

public record GetLiveStockUpdatesRequest(
    IReadOnlyList<int> ProductIds) : IStreamRequest<StockUpdate>;
```

---

## IStreamRequestHandler

Implementa `IStreamRequestHandler<TRequest, TResponse>` para cada petición de streaming:

```csharp
public class GetOrdersReportStreamHandler
    : IStreamRequestHandler<GetOrdersReportStreamRequest, OrderReportLine>
{
    private readonly IOrderRepository _orders;

    public GetOrdersReportStreamHandler(IOrderRepository orders) => _orders = orders;

    public async IAsyncEnumerable<OrderReportLine> Handle(
        GetOrdersReportStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Obtiene filas de forma lazy desde la base de datos
        await foreach (var order in _orders.StreamByDateRangeAsync(request.From, request.To, ct))
        {
            if (request.CustomerId is not null && order.CustomerId != request.CustomerId)
                continue;

            yield return new OrderReportLine(
                order.Id,
                order.CustomerId,
                order.Total,
                order.PlacedAt,
                order.Status);
        }
    }
}
```

El handler usa `yield return` para producir elementos de forma lazy. Cada elemento se produce en cuanto está disponible, sin cargar todo el resultado en memoria.

---

## CreateStream

Despacha una petición de streaming usando `IValiMediator.CreateStream`:

```csharp
IAsyncEnumerable<OrderReportLine> stream = _mediator.CreateStream(
    new GetOrdersReportStreamRequest(DateOnly.Parse("2025-01-01"), DateOnly.Parse("2025-12-31")),
    cancellationToken);

// Consumir el stream
await foreach (var line in stream.WithCancellation(cancellationToken))
{
    Console.WriteLine($"{line.OrderId}: {line.Total:C}");
}
```

`CreateStream` es lazy — el handler no empieza a ejecutarse hasta que comienzas a iterar con `await foreach`.

---

## Streaming en ASP.NET Core

### Minimal API

```csharp
app.MapGet("/reports/orders/stream", async (
    DateOnly from,
    DateOnly to,
    IValiMediator mediator,
    CancellationToken ct) =>
{
    var stream = mediator.CreateStream(new GetOrdersReportStreamRequest(from, to), ct);
    return Results.Ok(stream);  // ASP.NET Core serializa IAsyncEnumerable como array JSON
});
```

### Controller Action

```csharp
[HttpGet("export")]
public IAsyncEnumerable<ProductExportRow> ExportCatalog(
    [FromQuery] string? category,
    CancellationToken ct)
{
    // Devuelve IAsyncEnumerable directamente — ASP.NET Core hace streaming de la respuesta JSON
    return _mediator.CreateStream(
        new ExportProductCatalogRequest(category),
        ct);
}
```

### Respuesta de Streaming Manual

```csharp
[HttpGet("report")]
public async Task StreamOrderReport(
    [FromQuery] DateOnly from,
    [FromQuery] DateOnly to,
    CancellationToken ct)
{
    Response.ContentType = "application/x-ndjson";

    var stream = _mediator.CreateStream(new GetOrdersReportStreamRequest(from, to), ct);

    await foreach (var line in stream.WithCancellation(ct))
    {
        var json = JsonSerializer.Serialize(line);
        await Response.WriteAsync(json + "\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
```

---

## El Streaming Bypasa el Pipeline

> **Importante:** Los pipeline behaviors (`IPipelineBehavior`) y los processors (`IPreProcessor`, `IPostProcessor`) **no se aplican** a las peticiones de streaming. `CreateStream` llama al handler directamente.

Esto es por diseño: el contrato de streaming (`IAsyncEnumerable<T>`) es lazy y el pipeline está diseñado para pares request/response. Aplica los concerns transversales directamente en el handler o a nivel del consumidor.

```csharp
public class GetOrdersReportStreamHandler
    : IStreamRequestHandler<GetOrdersReportStreamRequest, OrderReportLine>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<GetOrdersReportStreamHandler> _logger;

    public GetOrdersReportStreamHandler(
        IOrderRepository orders,
        ILogger<GetOrdersReportStreamHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async IAsyncEnumerable<OrderReportLine> Handle(
        GetOrdersReportStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Aplica logging directamente en el handler (sin pipeline disponible)
        _logger.LogInformation(
            "Iniciando stream de reporte de pedidos para el período {From} a {To}.",
            request.From, request.To);

        var count = 0;

        await foreach (var order in _orders.StreamByDateRangeAsync(request.From, request.To, ct))
        {
            yield return new OrderReportLine(
                order.Id, order.CustomerId, order.Total, order.PlacedAt, order.Status);

            count++;
        }

        _logger.LogInformation("Stream de reporte completado. {Count} filas producidas.", count);
    }
}
```

---

## Cancelación

`CreateStream` acepta un `CancellationToken`. Cuando el token se cancela (por ejemplo, la conexión HTTP se cierra), la iteración con `await foreach` termina y el token se propaga al handler vía `[EnumeratorCancellation]`.

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var stream = _mediator.CreateStream(new ExportProductCatalogRequest(), cts.Token);

await foreach (var row in stream.WithCancellation(cts.Token))
{
    ProcessRow(row);
}
```

---

## Cuándo Usar Streaming

Usa `IStreamRequest<T>` cuando:

- El conjunto de resultados es **grande** y quieres evitar cargarlo todo en memoria
- El consumidor procesa resultados de **forma incremental** (por ejemplo, escribir en un archivo, mostrar progreso)
- Necesitas **datos en tiempo real** que llegan con el tiempo (por ejemplo, actualizaciones de precios en vivo)
- Quieres hacer **streaming de JSON** desde un endpoint de API al cliente

Usa `IRequest<IReadOnlyList<T>>` cuando:
- El conjunto de resultados es pequeño y cabe cómodamente en memoria
- El consumidor necesita la lista completa antes de empezar a procesar
- Necesitas que se apliquen pipeline behaviors (validación, caché) a la petición

---

## Siguientes Pasos

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Concerns transversales para peticiones (no para streams)
- **[Peticiones](04-peticiones.md)** — Patrón estándar de request/response
- **[Fire and Forget](06-fire-and-forget.md)** — Comandos unidireccionales para efectos secundarios
