# Vali-Mediator.Observability

Zero-dependency observability integration for [Vali-Mediator](https://github.com/UBF21/Vali-Mediator) (.NET 7 / 8 / 9).

Provides OpenTelemetry-compatible `ActivitySource` tracing, pluggable metrics via `IMetricsCollector`, and structured per-request lifecycle hooks via `IRequestObserver` — with no OpenTelemetry SDK dependency required.

---

## Installation

```bash
dotnet add package Vali-Mediator.Observability
```

---

## Quick Start

```csharp
// Program.cs
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddObservabilityBehavior();   // registers both request + dispatch behaviors
});

builder.Services
    .AddObservability()                  // registers NoOpMetricsCollector (replace below)
    .AddConsoleMetrics()                 // replace with ConsoleMetricsCollector (dev only)
    .AddConsoleLoggingObserver();        // add ConsoleLoggingObserver (dev only)
```

---

## Features

| Feature | API |
|---------|-----|
| OpenTelemetry-compatible tracing | `ValiMediatorDiagnostics.ActivitySource` — source name `"Vali-Mediator"` |
| Request lifecycle hooks | `IRequestObserver` — `OnStarted`, `OnCompleted`, `OnFailed` |
| Rich execution context | `ObservabilityContext` — name, operationId, duration, success, exception, request, response, tags |
| Pluggable metrics | `IMetricsCollector` — `RecordRequestStarted`, `RecordRequestCompleted`, `RecordRequestFailed` |
| No-op default | `NoOpMetricsCollector` — zero overhead when no metrics back-end is configured |
| Console output | `ConsoleMetricsCollector` + `ConsoleLoggingObserver` for dev/debug |
| IRequest pipeline | `ObservabilityBehavior<TRequest, TResponse>` |
| IDispatch pipeline | `ObservabilityDispatchBehavior<TRequest>` (INotification / IFireAndForget) |
| Multiple observers | All observers always run; exceptions collected into `AggregateException` |
| Fluent DI | `AddObservabilityBehavior`, `AddObservability`, `AddMetricsCollector<T>`, `AddRequestObserver<T>`, `AddConsoleMetrics`, `AddConsoleLoggingObserver` |

---

## OpenTelemetry Integration

No additional packages required inside this library. To enable tracing, add the source to your OpenTelemetry tracer provider:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddSource("Vali-Mediator")
        .AddOtlpExporter());
```

---

## Custom Metrics Collector

Implement `IMetricsCollector` and register it:

```csharp
public sealed class PrometheusMetricsCollector : IMetricsCollector
{
    private static readonly Counter RequestsStarted =
        Metrics.CreateCounter("vali_mediator_requests_started_total", "Total started requests", "request");

    public void RecordRequestStarted(string requestName) =>
        RequestsStarted.WithLabels(requestName).Inc();

    public void RecordRequestCompleted(string requestName, TimeSpan duration, bool success) { /* ... */ }
    public void RecordRequestFailed(string requestName, TimeSpan duration, string exceptionType) { /* ... */ }
}

// Registration:
builder.Services
    .AddObservability()
    .AddMetricsCollector<PrometheusMetricsCollector>();
```

---

## Custom Observer

```csharp
public sealed class AuditObserver : IRequestObserver
{
    private readonly IAuditService _audit;
    public AuditObserver(IAuditService audit) => _audit = audit;

    public Task OnStarted(ObservabilityContext ctx, CancellationToken ct = default) =>
        _audit.LogStartAsync(ctx.RequestName, ctx.OperationId, ct);

    public Task OnCompleted(ObservabilityContext ctx, CancellationToken ct = default) =>
        _audit.LogCompletedAsync(ctx.RequestName, ctx.Duration, ct);

    public Task OnFailed(ObservabilityContext ctx, CancellationToken ct = default) =>
        _audit.LogFailedAsync(ctx.RequestName, ctx.Exception, ct);
}

// Registration (multiple observers supported):
builder.Services.AddRequestObserver<AuditObserver>();
```

---

## ObservabilityContext Properties

| Property | Type | Description |
|----------|------|-------------|
| `RequestName` | `string` | `typeof(TRequest).Name` |
| `OperationId` | `string?` | Unique `Guid` per execution |
| `StartedAt` | `DateTimeOffset` | UTC start timestamp |
| `Duration` | `TimeSpan?` | Set after handler returns |
| `IsSuccess` | `bool` | `true` on success |
| `Exception` | `Exception?` | Set on failure |
| `Request` | `object?` | The original request object |
| `Response` | `object?` | The response (set on success) |
| `Tags` | `Dictionary<string, object?>` | Extensible metadata bag |

---

## Donations

If Vali-Mediator is useful to you, consider supporting its development:

- **Latin America** — [MercadoPago](https://link.mercadopago.com.pe/felipermm)
- **International** — [PayPal](https://paypal.me/felipeRMM?country.x=PE&locale.x=es_XC)

---

## License

[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)

## Contributions

Issues and pull requests are welcome on [GitHub](https://github.com/UBF21/Vali-Mediator).
