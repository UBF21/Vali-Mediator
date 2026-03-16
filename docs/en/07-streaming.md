# Streaming

Streaming requests return an asynchronous sequence of items via `IAsyncEnumerable<T>`. They are designed for scenarios where results are produced and consumed incrementally — reports, large datasets, real-time feeds, and server-sent events.

---

## IStreamRequest\<TResponse\>

Implement `IStreamRequest<TResponse>` to define a streaming request:

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

Implement `IStreamRequestHandler<TRequest, TResponse>` for each streaming request:

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
        // Fetch rows lazily from the database
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

The handler uses `yield return` to produce items lazily. Each item is yielded as it becomes available, without buffering the full result set in memory.

---

## CreateStream

Dispatch a streaming request using `IValiMediator.CreateStream`:

```csharp
IAsyncEnumerable<OrderReportLine> stream = _mediator.CreateStream(
    new GetOrdersReportStreamRequest(DateOnly.Parse("2025-01-01"), DateOnly.Parse("2025-12-31")),
    cancellationToken);

// Consume the stream
await foreach (var line in stream.WithCancellation(cancellationToken))
{
    Console.WriteLine($"{line.OrderId}: {line.Total:C}");
}
```

`CreateStream` is lazy — the handler does not start executing until you begin iterating with `await foreach`.

---

## Streaming in ASP.NET Core

### Minimal API with Server-Sent Events

```csharp
app.MapGet("/reports/orders/stream", async (
    DateOnly from,
    DateOnly to,
    IValiMediator mediator,
    CancellationToken ct) =>
{
    var stream = mediator.CreateStream(new GetOrdersReportStreamRequest(from, to), ct);
    return Results.Ok(stream);  // ASP.NET Core serializes IAsyncEnumerable as a JSON array
});
```

### Controller Action

```csharp
[HttpGet("export")]
public IAsyncEnumerable<ProductExportRow> ExportCatalog(
    [FromQuery] string? category,
    CancellationToken ct)
{
    // Return IAsyncEnumerable directly — ASP.NET Core streams the JSON response
    return _mediator.CreateStream(
        new ExportProductCatalogRequest(category),
        ct);
}
```

### Manual Streaming Response

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

## Streaming Bypasses the Pipeline

> **Important:** Pipeline behaviors (`IPipelineBehavior`) and processors (`IPreProcessor`, `IPostProcessor`) are **not applied** to streaming requests. `CreateStream` calls the handler directly.

This is by design: the streaming contract (`IAsyncEnumerable<T>`) is lazy and the pipeline is designed for request/response pairs. Apply cross-cutting concerns directly in the handler or at the consumer level.

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
        // Apply logging directly in the handler (no pipeline available)
        _logger.LogInformation(
            "Starting order report stream for period {From} to {To}.",
            request.From, request.To);

        var count = 0;

        await foreach (var order in _orders.StreamByDateRangeAsync(request.From, request.To, ct))
        {
            yield return new OrderReportLine(
                order.Id, order.CustomerId, order.Total, order.PlacedAt, order.Status);

            count++;
        }

        _logger.LogInformation("Order report stream completed. {Count} rows yielded.", count);
    }
}
```

---

## Cancellation

`CreateStream` accepts a `CancellationToken`. When the token is cancelled (e.g., the HTTP connection drops), the `await foreach` iteration terminates and the token propagates to the handler via `[EnumeratorCancellation]`.

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var stream = _mediator.CreateStream(new ExportProductCatalogRequest(), cts.Token);

await foreach (var row in stream.WithCancellation(cts.Token))
{
    ProcessRow(row);
}
```

---

## When to Use Streaming

Use `IStreamRequest<T>` when:

- The result set is **large** and you want to avoid loading everything into memory
- The consumer processes results **incrementally** (e.g., writing to a file, displaying progress)
- You need **real-time data** that arrives over time (e.g., live price updates)
- You want to **stream JSON** from an API endpoint to the client

Use `IRequest<IReadOnlyList<T>>` when:
- The result set is small and fits comfortably in memory
- The consumer needs the full list before it can begin processing
- You need pipeline behaviors (validation, caching) applied to the request

---

## Next Steps

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Cross-cutting concerns for requests (not streams)
- **[Requests](04-requests.md)** — Standard request/response pattern
- **[Fire and Forget](06-fire-and-forget.md)** — One-way side-effect commands
