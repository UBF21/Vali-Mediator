# Resiliencia

`Vali-Mediator.Resilience` es un paquete opcional que añade políticas de resiliencia al pipeline de Vali-Mediator. Las políticas se componen mediante un builder fluido y se aplican como un pipeline behavior transversal.

---

## Instalación

```bash
dotnet add package Vali-Mediator.Resilience
```

---

## Registro en DI

Agrega el behavior de resiliencia dentro de `AddValiMediator`:

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddResilienceBehavior();
});
```

`AddResilienceBehavior()` registra `ResiliencePipelineBehavior<,>` como el behavior más externo del pipeline de peticiones.

---

## ResiliencePolicy — Builder Fluido

`ResiliencePolicy` es el punto de entrada para construir políticas. Puedes especificar una clave de operación opcional para identificar la política en logs y métricas:

```csharp
var policy = ResiliencePolicy.Create("place-order")
    .Retry(3)
    .Timeout(TimeSpan.FromSeconds(10))
    .Build();
```

Cuando no se necesita identificador:

```csharp
var policy = ResiliencePolicy.Create()
    .CircuitBreaker(opts =>
    {
        opts.FailureThreshold = 0.5;
        opts.MinimumThroughput = 10;
        opts.BreakDuration = TimeSpan.FromSeconds(30);
    })
    .Build();
```

---

## Políticas Disponibles

### Retry

Reintenta la operación ante fallos transitorios.

**Forma simple:**

```csharp
var policy = ResiliencePolicy.Create()
    .Retry(maxRetries: 3)
    .Build();
```

**Forma avanzada:**

```csharp
var policy = ResiliencePolicy.Create()
    .Retry(opts =>
    {
        opts.MaxRetries = 5;
        opts.Delay = TimeSpan.FromMilliseconds(200);
        opts.BackoffType = RetryBackoffType.Exponential;
        opts.UseJitter = true;
        opts.ShouldHandle = ex => ex is HttpRequestException or TimeoutException;
    })
    .Build();
```

| Propiedad | Tipo | Descripción |
|---|---|---|
| `MaxRetries` | `int` | Número máximo de reintentos |
| `Delay` | `TimeSpan` | Retraso base entre reintentos |
| `BackoffType` | `RetryBackoffType` | `Constant`, `Linear`, `Exponential` |
| `UseJitter` | `bool` | Añade variación aleatoria al retraso |
| `ShouldHandle` | `Func<Exception, bool>` | Predicado de excepciones reintentables |

---

### Circuit Breaker

Abre el circuito cuando la tasa de fallos supera el umbral, impidiendo que las peticiones lleguen al handler degradado.

```csharp
var policy = ResiliencePolicy.Create()
    .CircuitBreaker(opts =>
    {
        opts.FailureThreshold = 0.5;
        opts.MinimumThroughput = 10;
        opts.SamplingDuration = TimeSpan.FromSeconds(60);
        opts.BreakDuration = TimeSpan.FromSeconds(30);
        opts.ShouldHandle = ex => ex is not OperationCanceledException;
        opts.OnOpened = args =>
        {
            logger.LogWarning("Circuito abierto. Duración: {Duration}", args.BreakDuration);
            return ValueTask.CompletedTask;
        };
        opts.OnClosed = _ =>
        {
            logger.LogInformation("Circuito cerrado.");
            return ValueTask.CompletedTask;
        };
    })
    .Build();
```

| Propiedad | Tipo | Descripción |
|---|---|---|
| `FailureThreshold` | `double` | Proporción de fallos que abre el circuito (0.0–1.0) |
| `MinimumThroughput` | `int` | Peticiones mínimas antes de evaluar el umbral |
| `SamplingDuration` | `TimeSpan` | Ventana de tiempo para calcular la tasa de fallos |
| `BreakDuration` | `TimeSpan` | Tiempo que permanece abierto el circuito |
| `ShouldHandle` | `Func<Exception, bool>` | Predicado de excepciones que cuentan como fallo |
| `OnOpened` | `Func<CircuitOpenedArgs, ValueTask>` | Callback al abrir el circuito |
| `OnClosed` | `Func<CircuitClosedArgs, ValueTask>` | Callback al cerrar el circuito |

Cuando el circuito está abierto, las peticiones lanzan `CircuitOpenException`.

---

### Timeout

Cancela la operación si supera el tiempo máximo.

**Forma simple:**

```csharp
var policy = ResiliencePolicy.Create()
    .Timeout(TimeSpan.FromSeconds(10))
    .Build();
```

**Forma avanzada:**

```csharp
var policy = ResiliencePolicy.Create()
    .Timeout(opts =>
    {
        opts.Timeout = TimeSpan.FromSeconds(5);
        opts.OnTimeout = args =>
        {
            logger.LogWarning("Operación cancelada por timeout: {Operation}", args.OperationKey);
            return ValueTask.CompletedTask;
        };
    })
    .Build();
```

Cuando se supera el tiempo límite, se lanza `TimeoutException`.

---

### Bulkhead

Limita el número de ejecuciones concurrentes. Las peticiones que superan la cola son rechazadas inmediatamente.

```csharp
var policy = ResiliencePolicy.Create()
    .Bulkhead(maxConcurrent: 10, maxQueued: 5)
    .Build();
```

| Parámetro | Descripción |
|---|---|
| `maxConcurrent` | Número máximo de ejecuciones concurrentes |
| `maxQueued` | Número máximo de peticiones en espera cuando se alcanza el límite de concurrencia |

Cuando se supera la capacidad del bulkhead, se lanza `BulkheadRejectedException`.

---

### Hedge

Envía peticiones adicionales (hedged) si la petición original no responde en el tiempo esperado. Devuelve la primera respuesta exitosa.

**Forma simple:**

```csharp
var policy = ResiliencePolicy.Create()
    .Hedge(hedgeDelay: TimeSpan.FromMilliseconds(300))
    .Build();
```

**Forma avanzada:**

```csharp
var policy = ResiliencePolicy.Create()
    .Hedge(opts =>
    {
        opts.HedgeDelay = TimeSpan.FromMilliseconds(500);
        opts.MaxHedgedAttempts = 2;
        opts.ShouldHandle = ex => ex is HttpRequestException;
    })
    .Build();
```

| Propiedad | Tipo | Descripción |
|---|---|---|
| `HedgeDelay` | `TimeSpan` | Tiempo de espera antes de lanzar el intento adicional |
| `MaxHedgedAttempts` | `int` | Número máximo de intentos hedged paralelos |
| `ShouldHandle` | `Func<Exception, bool>` | Predicado de excepciones que activan el hedge |

---

### Rate Limiter

Controla la tasa de peticiones aceptadas.

**Forma simple:**

```csharp
var policy = ResiliencePolicy.Create()
    .RateLimiter(bucketCapacity: 10)
    .Build();
```

**Forma avanzada:**

```csharp
var policy = ResiliencePolicy.Create()
    .RateLimiter(opts =>
    {
        opts.Algorithm = RateLimiterAlgorithm.SlidingWindow;
        opts.PermitLimit = 100;
        opts.Window = TimeSpan.FromSeconds(60);
        opts.QueueLimit = 0;
    })
    .Build();
```

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Algorithm` | `RateLimiterAlgorithm` | `TokenBucket`, `SlidingWindow`, `FixedWindow`, `Concurrency` |
| `PermitLimit` | `int` | Número máximo de permisos en la ventana |
| `Window` | `TimeSpan` | Duración de la ventana temporal |
| `QueueLimit` | `int` | Máximo de peticiones en cola cuando se agota el límite |

Cuando se agota el límite, se lanza `RateLimitExceededException`.

---

### Chaos

Inyecta fallos aleatorios para pruebas de resiliencia. Solo debe habilitarse en entornos de prueba o staging.

```csharp
var policy = ResiliencePolicy.Create()
    .Chaos(injectionRate: 0.1, opts =>
    {
        opts.ExceptionFactory = () => new Exception("chaos");
        opts.EnabledWhen = _ => Environment.GetEnvironmentVariable("CHAOS_ENABLED") == "true";
    })
    .Build();
```

| Parámetro | Tipo | Descripción |
|---|---|---|
| `injectionRate` | `double` | Probabilidad de inyectar un fallo (0.0–1.0) |
| `ExceptionFactory` | `Func<Exception>` | Fábrica de la excepción a inyectar |
| `EnabledWhen` | `Func<ResilienceContext, bool>` | Condición para activar el caos |

---

### Fallback

Define un valor o acción de recuperación cuando todas las demás políticas fallan.

```csharp
var policy = ResiliencePolicy.Create()
    .Retry(3)
    .Fallback<OrderResult>(opts =>
    {
        opts.FallbackValue = default;
        opts.ShouldHandle = ex => ex is HttpRequestException;
        opts.OnFallback = args =>
        {
            logger.LogError(args.Outcome.Exception, "Usando fallback para la operación.");
            return ValueTask.CompletedTask;
        };
    })
    .Build();
```

| Propiedad | Tipo | Descripción |
|---|---|---|
| `FallbackValue` | `T` | Valor devuelto en caso de fallo |
| `ShouldHandle` | `Func<Exception, bool>` | Predicado de excepciones que activan el fallback |
| `OnFallback` | `Func<FallbackArgs<T>, ValueTask>` | Callback ejecutado al activar el fallback |

---

## Ejecución Manual de Políticas

Las políticas pueden ejecutarse directamente sin el pipeline behavior.

**Con valor de retorno:**

```csharp
var result = await policy.ExecuteAsync<OrderResult>(
    async ct => await orderService.PlaceOrderAsync(command, ct),
    cancellationToken);
```

**Sin valor de retorno (void):**

```csharp
await policy.ExecuteAsync(
    async ct => await notificationService.SendAsync(notification, ct),
    cancellationToken);
```

---

## Interfaz IResilient — Políticas por Handler

Implementa `IResilient` en una petición para asociarle una política de resiliencia específica. El behavior la detecta automáticamente en ejecución:

```csharp
public class PlaceOrderCommand : IRequest<Result<int>>, IResilient
{
    public string CustomerId { get; init; }
    public List<OrderLine> Lines { get; init; }

    public ResiliencePolicy ResiliencePolicy => ResiliencePolicy.Create("place-order")
        .Retry(opts =>
        {
            opts.MaxRetries = 3;
            opts.BackoffType = RetryBackoffType.Exponential;
        })
        .CircuitBreaker(opts =>
        {
            opts.FailureThreshold = 0.5;
            opts.BreakDuration = TimeSpan.FromSeconds(30);
        })
        .Timeout(TimeSpan.FromSeconds(5))
        .Build();
}
```

Cuando la petición implementa `IResilient`, el behavior de resiliencia usa su política en lugar de la política global.

---

## Interfaz IHasTimeout — Timeout Declarativo

Para declarar un timeout directamente en la petición sin construir una política completa, implementa `IHasTimeout`:

```csharp
public class GetReportQuery : IRequest<Result<ReportDto>>, IHasTimeout
{
    public int ReportId { get; init; }

    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
}
```

Cuando la petición implementa `IHasTimeout` pero no `IResilient`, el behavior aplica únicamente la política de timeout declarada.

---

## Excepciones

| Excepción | Causa |
|---|---|
| `CircuitOpenException` | El circuito está abierto y rechaza peticiones |
| `BulkheadRejectedException` | Se superó el límite de concurrencia y la cola del bulkhead |
| `RateLimitExceededException` | Se agotaron los permisos del rate limiter |
| `TimeoutException` | La operación superó el tiempo límite configurado |

Todas heredan de `ValiResilienceException : ValiMediatorException`.

---

## Combinación de Políticas

Las políticas se componen con el builder fluido. Cada política envuelve a la siguiente según el orden de ejecución definido:

```csharp
var policy = ResiliencePolicy.Create("checkout")
    .Fallback<Result<int>>(opts =>
    {
        opts.FallbackValue = Result<int>.Fail("Servicio no disponible.", ErrorType.Failure);
    })
    .RateLimiter(opts =>
    {
        opts.Algorithm = RateLimiterAlgorithm.TokenBucket;
        opts.PermitLimit = 50;
        opts.Window = TimeSpan.FromSeconds(10);
    })
    .Timeout(TimeSpan.FromSeconds(8))
    .CircuitBreaker(opts =>
    {
        opts.FailureThreshold = 0.4;
        opts.MinimumThroughput = 5;
        opts.BreakDuration = TimeSpan.FromSeconds(20);
    })
    .Bulkhead(maxConcurrent: 20, maxQueued: 10)
    .Retry(opts =>
    {
        opts.MaxRetries = 3;
        opts.BackoffType = RetryBackoffType.Exponential;
        opts.UseJitter = true;
    })
    .Hedge(opts =>
    {
        opts.HedgeDelay = TimeSpan.FromMilliseconds(400);
        opts.MaxHedgedAttempts = 1;
    })
    .Build();
```

---

## Orden de Ejecución de las Políticas

El orden en que las políticas evalúan la petición es fijo, independientemente del orden en que se registren en el builder:

```
Fallback
  └─ Chaos
       └─ RateLimiter
            └─ Timeout
                 └─ Circuit Breaker
                      └─ Bulkhead
                           └─ Retry
                                └─ Hedge
                                     └─ delegate (handler)
```

**Lectura del orden:** Fallback es la capa más externa — captura cualquier fallo que no hayan resuelto las políticas internas. El delegate (handler real) es el núcleo de la cadena y la última capa en ejecutarse.

---

## Registro Global en Program.cs

```csharp
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Agrega el behavior de resiliencia como capa más externa
    config.AddResilienceBehavior();

    // Otros behaviors se registran después
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
});

var app = builder.Build();
app.Run();
```

---

## Siguientes Pasos

- **[Caching](15-caching.md)** — Cache distribuida y en memoria para peticiones
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Behaviors personalizados en el pipeline
- **[Result](10-resultado.md)** — Tipos Result para respuestas tipadas de handlers
