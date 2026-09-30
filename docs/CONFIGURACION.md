# Configuración y límites

Todos los límites de Vali-Mediator se cambian al **registrar** la librería, con un `Action<TOptions>`. La librería no depende de `Microsoft.Extensions.Options`: para leer desde `appsettings.json` se enlaza el objeto de opciones con `Bind`.

Los valores se validan **al configurarlos** (en el setter): un valor inválido lanza `ArgumentOutOfRangeException` con el nombre de la propiedad al arrancar la aplicación, no en la primera petición. Las opciones de cada policy de Resilience se validan en `Build()`.

## Enlazar desde `appsettings.json`

```json
{
  "ValiMediator": {
    "SendAllMaxDegreeOfParallelism": 8,
    "Cache":       { "MaxEntries": 50000, "MaxKeyLength": 256, "CleanupInterval": "00:01:00" },
    "Caching":     { "CoalescingWaitTimeout": "00:00:10" },
    "Idempotency": { "MaxKeyLength": 128, "VerifyRequestFingerprint": true },
    "IdempotencyStore": { "MaxEntries": 20000, "DefaultExpiration": "12:00:00" },
    "Resilience":  { "MaxSharedStates": 500 },
    "Observability": { "IncludeExceptionMessage": false },
    "Http":        { "ExposeErrorDetails": false }
  }
}
```

```csharp
var cfg = builder.Configuration.GetSection("ValiMediator");

builder.Services.AddValiMediator(c =>
{
    c.RegisterServicesFromAssemblyContaining<Program>();
    c.SendAllMaxDegreeOfParallelism = cfg.GetValue<int?>("SendAllMaxDegreeOfParallelism");
});

builder.Services.AddInMemoryCacheStore(o => cfg.GetSection("Cache").Bind(o));
builder.Services.AddCachingOptions(o => cfg.GetSection("Caching").Bind(o));
builder.Services.AddInMemoryIdempotencyStore(o => cfg.GetSection("IdempotencyStore").Bind(o));
builder.Services.AddIdempotencyOptions(o => cfg.GetSection("Idempotency").Bind(o));
builder.Services.AddResilienceOptions(o => cfg.GetSection("Resilience").Bind(o));
builder.Services.AddObservability(o => cfg.GetSection("Observability").Bind(o));
builder.Services.AddResultHttpOptions(o => cfg.GetSection("Http").Bind(o));
builder.Services.AddInMemoryDeadLetterQueue(cfg.GetValue("DeadLetterMaxEntries", 1_000));
```

Las policies de Resilience se configuran igual dentro del builder: `ResiliencePolicy.Create().Retry(o => cfg.GetSection("Retry").Bind(o))`.

## Core (`Vali-Mediator`)

| Opción | Registro | Default | Rango válido | Efecto |
|---|---|---|---|---|
| `SendAllMaxDegreeOfParallelism` | `AddValiMediator(c => ...)` | `null` (todo a la vez) | `null` o `>= 1` | Límite de peticiones en vuelo de `SendAll(requests)` cuando no se pasa uno explícito. El overload `SendAll(requests, max)` lo sobrescribe. |
| `maxEntries` del DLQ | `AddInMemoryDeadLetterQueue(maxEntries)` | `1000` | `>= 1` | Máximo de fallos retenidos; al superarlo se descartan los más antiguos. |

## Caching

| Opción | Registro | Default | Rango válido | Efecto |
|---|---|---|---|---|
| `MaxEntries` | `AddInMemoryCacheStore(o => ...)` | `10000` | `>= 1` | Capacidad del store; al llenarse se expulsa la entrada menos usada (LRU). |
| `MaxKeyLength` | ídem | `512` | `>= 1` | Claves (y nombres de grupo) más largas no se guardan; el handler igual se ejecuta. |
| `MaxGroups` | ídem | `10000` | `>= 1` | Grupos de invalidación distintos; al superarlo la entrada no se guarda (no queda sin poder invalidarse). |
| `MaxKeysPerGroup` | ídem | `10000` | `>= 1` | Claves por grupo; mismo criterio. |
| `CleanupInterval` | ídem | `5 min` | `> 0` | Frecuencia máxima del barrido de entradas vencidas (perezoso, al escribir). |
| `TimeProvider` | ídem | `TimeProvider.System` | no nulo | Reloj (pruebas). No se enlaza desde configuración. |
| `CoalescingWaitTimeout` | `AddCachingOptions(o => ...)` | `30 s` | `> 0` o `Timeout.InfiniteTimeSpan` | Espera máxima de peticiones que comparten un miss antes de ejecutar el handler ellas mismas. |

## Idempotency

| Opción | Registro | Default | Rango válido | Efecto |
|---|---|---|---|---|
| `MaxKeyLength` | `AddIdempotencyOptions(o => ...)` | `256` | `>= 1` | Largo máximo de `IdempotencyKey` e `IdempotencyScope` (cada uno); más largo lanza `ArgumentException`. |
| `VerifyRequestFingerprint` | ídem | `true` | `bool` | Compara el SHA-256 del request; misma clave con otro payload = conflicto. Desactivar con campos volátiles. |
| `MaxEntries` | `AddInMemoryIdempotencyStore(o => ...)` | `10000` | `>= 1` | Capacidad del store; se expulsan las más antiguas. |
| `DefaultExpiration` | ídem | `24 h` | `null` o `> 0` | Expiración de entradas sin una propia. `null` = hasta ser expulsadas. |

## Observability

| Opción | Registro | Default | Rango válido | Efecto |
|---|---|---|---|---|
| `IncludeExceptionMessage` | `AddObservability(o => ...)` | `false` | `bool` | Si es `false`, la telemetría y `ConsoleLoggingObserver` solo exponen el tipo de la excepción, no su mensaje. |

## AspNetCore

| Opción | Registro | Default | Rango válido | Efecto |
|---|---|---|---|---|
| `ExposeErrorDetails` | `AddResultHttpOptions(o => ...)` | `false` | `bool` | Si es `false`, los 500 llevan un detalle genérico. Los 4xx siempre muestran su mensaje. Se aplica con `result.ToActionResult(HttpContext)` / `result.ToHttpResult(HttpContext)`; sin registro rige el default. |

## Resilience

| Opción | Registro | Default | Rango válido | Efecto |
|---|---|---|---|---|
| `MaxSharedStates` | `AddResilienceOptions(o => ...)` | `10000` | `>= 1` | Claves distintas de `WithSharedState`; al superarlo se lanza `InvalidOperationException`. Es global al proceso; la última llamada gana. |
| `Retry.MaxRetries` | `.Retry(o => ...)` | `3` | `>= 0` | Reintentos. |
| `Retry.InitialDelay` / `MaxDelay` | ídem | `200 ms` / `30 s` | `>= 0` | Backoff; el retardo se limita a `MaxDelay` antes de construir el `TimeSpan`. |
| `Retry.Multiplier` | ídem | `2.0` | finito `> 0` | Factor del backoff exponencial. |
| `CircuitBreaker.FailureThreshold` | `.CircuitBreaker(o => ...)` | `5` | `>= 1` | Fallos para abrir. |
| `CircuitBreaker.FailureRateThreshold` | ídem | `0` | `0..1` | Tasa de fallos para abrir (con `MinimumThroughput`). |
| `CircuitBreaker.MinimumThroughput` | ídem | `10` | `>= 1` | Llamadas mínimas antes de evaluar la tasa. |
| `CircuitBreaker.SamplingDuration` / `BreakDuration` | ídem | `60 s` / `30 s` | `> 0` | Ventana de muestreo y tiempo abierto. |
| `CircuitBreaker.HalfOpenMaxAttempts` | ídem | `1` | `>= 1` | Sondas simultáneas en HalfOpen. |
| `Timeout.Timeout` | `.Timeout(o => ...)` | `30 s` | `> 0` | Tiempo máximo por intento. |
| `Bulkhead.MaxConcurrentCalls` | `.Bulkhead(o => ...)` | `10` | `>= 1` | Llamadas simultáneas. |
| `Bulkhead.MaxQueuedCalls` | ídem | `0` | `>= 0` | Cola de espera; `0` rechaza al instante. |
| `Bulkhead.QueueTimeout` | ídem | infinito | `>= 0` o infinito | Espera máxima en cola. |
| `Hedge.HedgeDelay` / `MaxHedgedAttempts` | `.Hedge(o => ...)` | `1 s` / `1` | `>= 0` | Retardo y máximo de intentos adicionales. |
| `Chaos.InjectionRate` | `.Chaos(o => ...)` | `0` | `0..1` | Probabilidad de inyección. |
| `RateLimiter.BucketCapacity` / `TokensPerInterval` / `ReplenishmentInterval` | `.RateLimiter(o => ...)` | `10` / `5` / `1 s` | `>= 0` / `>= 0` / `> 0` | Token bucket; `0` capacidad = rechazar todo, `0` tokens = sin reposición. |
| `RateLimiter.PermitLimit` / `Window` | ídem (`SlidingWindow`) | `100` / `1 s` | `>= 0` / `> 0` | Ventana deslizante. |
| `RateLimiter.QueueTimeout` | ídem | `0` | `>= 0` | Espera de un permiso. |
| `RateLimiter.MaxPartitions` | ídem | `10000` | `>= 1` | Particiones activas; las claves extra comparten un limitador de desborde. |
| `RateLimiter.PartitionIdleTimeout` | ídem | `5 min` | `>= 0` | Inactividad tras la cual se libera una partición. |

Los valores límite (`1`, `int.MaxValue`, `0` donde se permite) y los inválidos están cubiertos por tests parametrizados en cada proyecto `*.Tests`.
