# Observability

`Vali-Mediator.Observability` provides distributed tracing, metrics collection, and request observation for Vali-Mediator pipelines. It is built on top of the standard .NET `ActivitySource` API and is fully compatible with OpenTelemetry without adding a dependency on it.

---

## Installation

```bash
dotnet add package Vali-Mediator.Observability
```

---

## DI Setup

Register the observability services in `Program.cs`:

```csharp
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Observability;

builder.Services.AddObservability();

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Add observability as the outermost pipeline behavior
    config.AddObservabilityBehavior();
});
```

`AddObservability()` registers:
- `ValiMediatorDiagnostics` (singleton)
- `IMetricsCollector` — defaults to `NoOpMetricsCollector`
- `IRequestObserver` collection — empty by default

`AddObservabilityBehavior()` registers `ObservabilityBehavior<,>` as the outermost request pipeline behavior. Place it first in the behavior registration order so it wraps all other behaviors.

---

## Distributed Tracing

### ActivitySource

The package exposes a single static `ActivitySource` named `"Vali-Mediator"` at version `2.0.0`:

```csharp
using Vali_Mediator.Observability.Diagnostics;

// Access the shared ActivitySource
ActivitySource source = ValiMediatorDiagnostics.ActivitySource;

// Start a named activity manually
using Activity? activity = ValiMediatorDiagnostics.StartActivity("MyRequest");
```

`StartActivity` returns `null` when no listener is active, which is the standard .NET behavior — no activity is created when nothing is listening.

### OpenTelemetry Integration

Wire the `"Vali-Mediator"` source into your OpenTelemetry tracing pipeline. No extra package is required beyond `OpenTelemetry.Extensions.Hosting` and your chosen exporter:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("Vali-Mediator")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(); // or AddJaegerExporter(), AddConsoleExporter(), etc.
    });
```

Each request handled through the pipeline creates one `Activity` tagged with:

| Tag | Value |
|---|---|
| `request.name` | `typeof(TRequest).Name` |
| `request.type` | Full type name of the request |
| `result.success` | `true` or `false` |
| `error.type` | `ErrorType` name (only on failure) |

---

## Metrics Collection

### IMetricsCollector

Implement `IMetricsCollector` to collect timing and outcome data for every handled request:

```csharp
using Vali_Mediator.Observability.Metrics;

public interface IMetricsCollector
{
    void RecordRequest(
        string requestName,
        TimeSpan duration,
        bool success,
        string? errorType = null);
}
```

| Parameter | Description |
|---|---|
| `requestName` | `typeof(TRequest).Name` |
| `duration` | Total time from pipeline entry to exit, including all behaviors |
| `success` | `true` when the handler completed without exception and `Result.IsSuccess` is `true` |
| `errorType` | `ErrorType` name (e.g., `"Validation"`, `"NotFound"`) or `null` on success |

### Built-in Implementations

| Type | Description |
|---|---|
| `NoOpMetricsCollector` | Default. Records nothing. Zero allocation. |
| `ConsoleMetricsCollector` | Writes one line per request to the console. Suitable for development. |

### Custom Collector

Register a custom implementation to integrate with Prometheus, StatsD, Application Insights, or any other metrics system:

```csharp
// Custom implementation
public class PrometheusMetricsCollector : IMetricsCollector
{
    private static readonly Histogram RequestDuration = Metrics
        .CreateHistogram(
            "vali_mediator_request_duration_seconds",
            "Duration of Vali-Mediator requests.",
            new HistogramConfiguration
            {
                LabelNames = new[] { "request", "success", "error_type" }
            });

    public void RecordRequest(
        string requestName,
        TimeSpan duration,
        bool success,
        string? errorType = null)
    {
        RequestDuration
            .WithLabels(requestName, success.ToString(), errorType ?? string.Empty)
            .Observe(duration.TotalSeconds);
    }
}

// Registration
builder.Services.AddMetricsCollector<PrometheusMetricsCollector>();
```

`AddMetricsCollector<T>()` replaces the default `NoOpMetricsCollector` with your implementation (registered as Singleton).

### Console Metrics (Development Shortcut)

```csharp
builder.Services.AddConsoleMetrics();
```

Equivalent to `AddMetricsCollector<ConsoleMetricsCollector>()`. Use during development or in integration tests.

---

## Request Observers

Request observers receive structured context after each request completes or fails. Unlike behaviors, observers are fire-and-forget and do not participate in the pipeline chain — they cannot modify the response or short-circuit execution.

### ObservabilityContext

All observer methods receive an `ObservabilityContext`:

```csharp
using Vali_Mediator.Observability.Observers;

public sealed class ObservabilityContext
{
    public Type RequestType { get; init; }
    public string RequestName { get; init; }
    public TimeSpan Duration { get; init; }
    public bool IsSuccess { get; init; }
    public object? Response { get; init; }
    public Exception? Exception { get; init; }
    public string CorrelationId { get; init; }
}
```

| Property | Description |
|---|---|
| `RequestType` | `typeof(TRequest)` — the concrete request type |
| `RequestName` | `typeof(TRequest).Name` |
| `Duration` | Total elapsed time including all pipeline behaviors |
| `IsSuccess` | `true` when no exception was thrown and `Result.IsSuccess` is `true` (if applicable) |
| `Response` | The raw handler response as `object?`. Cast to the expected type when needed. |
| `Exception` | Populated when the handler or a behavior threw an unhandled exception. `null` on success. |
| `CorrelationId` | `Activity.Current?.TraceId.ToString()` or a generated GUID when no active `Activity` exists |

### IRequestObserver

```csharp
public interface IRequestObserver
{
    Task OnStarted(ObservabilityContext context, CancellationToken ct);
    Task OnCompleted(ObservabilityContext context, CancellationToken ct);
    Task OnFailed(ObservabilityContext context, CancellationToken ct);
}
```

| Method | Called when |
|---|---|
| `OnStarted` | Immediately before the inner pipeline executes |
| `OnCompleted` | After the handler succeeds (no exception, `IsSuccess` is `true`) |
| `OnFailed` | After the handler returns a failure result or throws an exception |

### Registering Observers

Multiple observers can be registered and all will be invoked for every request:

```csharp
builder.Services.AddRequestObserver<AuditObserver>();
builder.Services.AddRequestObserver<MetricsDashboardObserver>();
builder.Services.AddRequestObserver<AlertingObserver>();
```

Each observer is resolved from the DI container per pipeline execution and can take constructor dependencies.

### Console Logging Observer (Development Shortcut)

```csharp
builder.Services.AddConsoleLoggingObserver();
```

Registers a built-in observer that writes structured output to the console for every started, completed, and failed request. Use during development or in integration tests — not in production.

---

## Custom Observer Example

The following observer logs structured information using `ILogger`:

```csharp
using Microsoft.Extensions.Logging;
using Vali_Mediator.Observability.Observers;

public class LoggingRequestObserver : IRequestObserver
{
    private readonly ILogger<LoggingRequestObserver> _logger;

    public LoggingRequestObserver(ILogger<LoggingRequestObserver> logger)
        => _logger = logger;

    public Task OnStarted(ObservabilityContext context, CancellationToken ct)
    {
        _logger.LogInformation(
            "Request started. Name={RequestName} CorrelationId={CorrelationId}",
            context.RequestName,
            context.CorrelationId);

        return Task.CompletedTask;
    }

    public Task OnCompleted(ObservabilityContext context, CancellationToken ct)
    {
        _logger.LogInformation(
            "Request completed. Name={RequestName} Duration={DurationMs}ms CorrelationId={CorrelationId}",
            context.RequestName,
            context.Duration.TotalMilliseconds,
            context.CorrelationId);

        return Task.CompletedTask;
    }

    public Task OnFailed(ObservabilityContext context, CancellationToken ct)
    {
        if (context.Exception is not null)
        {
            _logger.LogError(
                context.Exception,
                "Request failed with exception. Name={RequestName} Duration={DurationMs}ms CorrelationId={CorrelationId}",
                context.RequestName,
                context.Duration.TotalMilliseconds,
                context.CorrelationId);
        }
        else
        {
            _logger.LogWarning(
                "Request returned failure. Name={RequestName} Duration={DurationMs}ms CorrelationId={CorrelationId}",
                context.RequestName,
                context.Duration.TotalMilliseconds,
                context.CorrelationId);
        }

        return Task.CompletedTask;
    }
}
```

Register it:

```csharp
builder.Services.AddRequestObserver<LoggingRequestObserver>();
```

---

## Complete Program.cs Example

```csharp
using OpenTelemetry.Trace;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Observability;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry — wire in the "Vali-Mediator" ActivitySource
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("Vali-Mediator")
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter();
    });

// Observability services
builder.Services.AddObservability();

// Custom metrics collector (e.g., Prometheus)
builder.Services.AddMetricsCollector<PrometheusMetricsCollector>();

// Request observers
builder.Services.AddRequestObserver<LoggingRequestObserver>();
builder.Services.AddRequestObserver<AuditObserver>();

// Vali-Mediator
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Observability must be outermost so it captures total duration
    config.AddObservabilityBehavior();

    // Other behaviors run inside observability
    config.AddRequestBehavior<ValidationBehavior<,>>();
    config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

---

## Summary

| Component | Interface / Type | Default |
|---|---|---|
| Tracing | `ValiMediatorDiagnostics.ActivitySource` | `ActivitySource("Vali-Mediator", "2.0.0")` |
| Metrics | `IMetricsCollector` | `NoOpMetricsCollector` |
| Observers | `IRequestObserver` | None (empty collection) |
| Pipeline hook | `ObservabilityBehavior<,>` | Registered via `AddObservabilityBehavior()` |

| Registration method | Effect |
|---|---|
| `services.AddObservability()` | Registers core observability infrastructure |
| `config.AddObservabilityBehavior()` | Adds pipeline behavior that drives tracing, metrics, and observers |
| `services.AddMetricsCollector<T>()` | Replaces the default `NoOpMetricsCollector` |
| `services.AddRequestObserver<T>()` | Adds an observer; multiple observers are supported |
| `services.AddConsoleMetrics()` | Development shortcut for `ConsoleMetricsCollector` |
| `services.AddConsoleLoggingObserver()` | Development shortcut for built-in console logging observer |

---

## Next Steps

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Understand behavior registration order
- **[Dependency Injection](12-dependency-injection.md)** — Full registration reference
- **[Result](10-result.md)** — How `Result<T>` failure state maps to observer context
