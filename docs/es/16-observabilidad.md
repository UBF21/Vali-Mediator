# Observabilidad

El paquete `Vali-Mediator.Observability` agrega soporte de trazas distribuidas, métricas y observers al pipeline de Vali-Mediator. No introduce dependencia directa en OpenTelemetry — expone un `ActivitySource` estándar de .NET que cualquier backend compatible puede consumir.

---

## Instalacion

```bash
dotnet add package Vali-Mediator.Observability
```

---

## Configuracion en el Contenedor de DI

Registra los servicios de observabilidad en dos pasos: primero agrega los servicios base, luego agrega el behavior al pipeline.

```csharp
// Program.cs
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddObservability();

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // El behavior de observabilidad debe ser el mas externo del pipeline
    config.AddObservabilityBehavior();
});
```

`AddObservability()` registra `ValiMediatorDiagnostics`, el `IMetricsCollector` por defecto (`NoOpMetricsCollector`), y la infraestructura de observers.

---

## Trazas con ActivitySource

El paquete declara un `ActivitySource` con nombre `"Vali-Mediator"` y version `2.0.0`. Es completamente compatible con OpenTelemetry y con cualquier listener que utilice el API de `System.Diagnostics`.

### API de diagnosticos

```csharp
// ValiMediatorDiagnostics (estatico)
public static class ValiMediatorDiagnostics
{
    // ActivitySource que emite todas las actividades del mediator
    public static ActivitySource ActivitySource { get; }

    // Inicia una actividad con el nombre de la peticion como nombre de operacion
    public static Activity? StartActivity(string requestName);
}
```

### Uso manual

Si necesitas crear actividades fuera del pipeline automatico, usa `StartActivity` directamente:

```csharp
public class MyService
{
    public async Task DoWork(string requestName)
    {
        using var activity = ValiMediatorDiagnostics.StartActivity(requestName);
        activity?.SetTag("custom.tag", "value");

        // ... logica de negocio
    }
}
```

---

## Integracion con OpenTelemetry

Para exportar las trazas a Jaeger, Zipkin, OTLP u otro backend, agrega el source al builder de tracing de OpenTelemetry:

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.Console
```

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("Vali-Mediator")
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService("MyApp"))
            .AddConsoleExporter();   // sustituir por el exporter real
    });
```

`Vali-Mediator.Observability` no referencia ningun paquete de OpenTelemetry. La integracion se realiza en la aplicacion del consumidor, lo que evita conflictos de version.

---

## Metricas con IMetricsCollector

### Interfaz

```csharp
public interface IMetricsCollector
{
    void RecordRequest(
        string name,
        TimeSpan duration,
        bool success,
        string? errorType = null);
}
```

| Parametro | Descripcion |
|---|---|
| `name` | Nombre del tipo de peticion (e.g. `"PlaceOrderCommand"`) |
| `duration` | Tiempo total de ejecucion del handler incluido el pipeline |
| `success` | `true` si no se lanzo ninguna excepcion |
| `errorType` | Nombre del `ErrorType` cuando el resultado es un fallo; `null` en exito |

### Implementaciones incluidas

| Clase | Comportamiento |
|---|---|
| `NoOpMetricsCollector` | Descarta todas las metricas. Es el default cuando no se registra otro collector. |
| `ConsoleMetricsCollector` | Imprime cada metrica en `Console.WriteLine`. Util para desarrollo y debug. |

### Registrar el collector de consola

```csharp
builder.Services.AddObservability();
builder.Services.AddConsoleMetrics();   // sobreescribe NoOpMetricsCollector
```

### Collector personalizado

Implementa `IMetricsCollector` y registralo con `AddMetricsCollector<T>()`:

```csharp
public class PrometheusMetricsCollector : IMetricsCollector
{
    private static readonly Counter RequestTotal = Metrics.CreateCounter(
        "vali_mediator_requests_total",
        "Total de peticiones procesadas por Vali-Mediator.",
        new CounterConfiguration { LabelNames = new[] { "request", "success", "error_type" } });

    private static readonly Histogram RequestDuration = Metrics.CreateHistogram(
        "vali_mediator_request_duration_seconds",
        "Duracion de las peticiones de Vali-Mediator en segundos.",
        new HistogramConfiguration { LabelNames = new[] { "request" } });

    public void RecordRequest(string name, TimeSpan duration, bool success, string? errorType = null)
    {
        RequestTotal.WithLabels(name, success.ToString(), errorType ?? "none").Inc();
        RequestDuration.WithLabels(name).Observe(duration.TotalSeconds);
    }
}
```

```csharp
builder.Services.AddObservability();
builder.Services.AddMetricsCollector<PrometheusMetricsCollector>();
```

---

## Observers con IRequestObserver

Los observers permiten reaccionar a eventos del ciclo de vida de cada peticion sin modificar el pipeline ni el handler.

### Interfaz

```csharp
public interface IRequestObserver
{
    Task OnStarted(ObservabilityContext context);
    Task OnCompleted(ObservabilityContext context);
    Task OnFailed(ObservabilityContext context);
}
```

### ObservabilityContext

```csharp
public sealed class ObservabilityContext
{
    // Tipo CLR de la peticion (e.g. typeof(PlaceOrderCommand))
    public Type RequestType { get; init; }

    // Nombre corto del tipo (e.g. "PlaceOrderCommand")
    public string RequestName { get; init; }

    // Duracion total hasta el momento en que se invoca el metodo del observer
    public TimeSpan Duration { get; init; }

    // true cuando el handler completo sin excepcion
    public bool IsSuccess { get; init; }

    // Respuesta devuelta por el handler; null en OnStarted y en caso de fallo
    public object? Response { get; init; }

    // Excepcion capturada; null en OnStarted y en caso de exito
    public Exception? Exception { get; init; }

    // Identificador de correlacion propagado desde la actividad actual o generado
    public string CorrelationId { get; init; }
}
```

### Cuando se invoca cada metodo

| Metodo | Momento |
|---|---|
| `OnStarted` | Inmediatamente antes de invocar el siguiente paso del pipeline |
| `OnCompleted` | Despues de que el handler devuelve sin excepcion |
| `OnFailed` | Despues de capturar una excepcion en el pipeline |

### Observer personalizado con ILogger

```csharp
using Microsoft.Extensions.Logging;
using Vali_Mediator.Observability;

public class StructuredLoggingObserver : IRequestObserver
{
    private readonly ILogger<StructuredLoggingObserver> _logger;

    public StructuredLoggingObserver(ILogger<StructuredLoggingObserver> logger)
        => _logger = logger;

    public Task OnStarted(ObservabilityContext context)
    {
        _logger.LogInformation(
            "Iniciando {RequestName} [correlationId={CorrelationId}].",
            context.RequestName,
            context.CorrelationId);

        return Task.CompletedTask;
    }

    public Task OnCompleted(ObservabilityContext context)
    {
        _logger.LogInformation(
            "Completado {RequestName} en {ElapsedMs}ms [correlationId={CorrelationId}].",
            context.RequestName,
            context.Duration.TotalMilliseconds,
            context.CorrelationId);

        return Task.CompletedTask;
    }

    public Task OnFailed(ObservabilityContext context)
    {
        _logger.LogError(
            context.Exception,
            "Fallo en {RequestName} tras {ElapsedMs}ms [correlationId={CorrelationId}].",
            context.RequestName,
            context.Duration.TotalMilliseconds,
            context.CorrelationId);

        return Task.CompletedTask;
    }
}
```

### Registrar observers

Se pueden registrar multiples observers. Todos se ejecutan para cada evento.

```csharp
builder.Services.AddObservability();
builder.Services.AddRequestObserver<StructuredLoggingObserver>();
builder.Services.AddRequestObserver<AuditTrailObserver>();
```

### Observer de consola (desarrollo y debug)

```csharp
builder.Services.AddObservability();
builder.Services.AddConsoleLoggingObserver();
```

---

## Configuracion Completa en Program.cs

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Observability;

var builder = WebApplication.CreateBuilder(args);

// Observabilidad
builder.Services.AddObservability();
builder.Services.AddRequestObserver<StructuredLoggingObserver>();
builder.Services.AddMetricsCollector<PrometheusMetricsCollector>();

// OpenTelemetry — solo en la aplicacion consumidora, no en el paquete
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("Vali-Mediator")
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("OrdersApi"))
            .AddOtlpExporter();
    });

// Mediator
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddObservabilityBehavior();              // primero = mas externo
    config.AddRequestBehavior<ValidationBehavior<,>>();
});

var app = builder.Build();
app.MapControllers();
app.Run();
```

---

## Siguientes Pasos

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Agregar logica transversal personalizada al pipeline
- **[Idempotencia](17-idempotencia.md)** — Evitar ejecuciones duplicadas de handlers
- **[Inyeccion de Dependencias](12-inyeccion-dependencias.md)** — Referencia completa de registro
