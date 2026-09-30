# Guía de migración 2.x → 3.0

Core `Vali-Mediator` **3.0.0**; extensiones (`AspNetCore`, `Caching`, `Idempotency`, `Observability`, `Resilience`) **2.0.0**. Las extensiones requieren `Vali-Mediator >= 3.0.0`; usa el rango `[3.0.0, 4.0.0)`. Actualiza el core y **todas** las extensiones a la vez: una extensión antigua contra el core 3.x falla en runtime con `MissingMethodException`.

## 1. Breaking obligatorio

### `next` recibe un `CancellationToken`

```csharp
// 2.x
public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken ct)
    => await next();

// 3.0
public async Task<TResponse> Handle(TRequest request, Func<CancellationToken, Task<TResponse>> next, CancellationToken ct)
    => await next(ct);   // o un token enlazado si el behavior cancela por su cuenta
```

Igual para `IPipelineBehavior<TDispatch>`: `Func<CancellationToken, Task> next`. Las lambdas que ignoraban el token pasan a `_ => ...`.

## 2. Cambios de comportamiento

| Área | Antes | Ahora | Acción |
|---|---|---|---|
| `Publish<T>` | Solo handlers del tipo estático | Unión de handlers del tipo estático y del runtime (sin duplicados) | Revisa handlers de tipos base/`INotification` que antes no corrían |
| Escaneo de assemblies | Escanear dos veces duplicaba handlers; gana el primero | Un solo registro; gana el último lifetime | Ninguna, salvo que dependieras del primero |
| `default(Result)` / `default(Result<T>)` | `Error = null`, `ErrorType.None` | Fallo "not initialized", `ErrorType.Failure`. `Fail("x", ErrorType.None)` conserva `None` | No uses `default` como éxito |
| `TimeoutBehavior` | El handler seguía corriendo | Cancela el token del handler; `TimeoutException` con la interna como `InnerException` | Pasa el token a tus operaciones |
| Excepciones de handlers | `TargetInvocationException` | Tipo y stack originales | Quita los `catch (TargetInvocationException)` |
| Resilience: orden | Fallback → Retry → Timeout → CB → Bulkhead → Hedge → RateLimiter → Chaos | **Fallback → Chaos → RateLimiter → Retry → Timeout → CB → Bulkhead → Hedge** | Un rechazo del rate limiter ya no se reintenta ni abre el circuito |
| Resilience: policy | Cacheada por tipo (gana la primera request) | Se resuelve por request | Reutiliza una instancia o usa `.WithSharedState("clave")` para compartir Bulkhead/RateLimiter/CB entre policies construidas por request |
| Resilience: Hedge | Devolvía `default` si todos fallaban | Lanza la última excepción; los perdedores se cancelan | Maneja la excepción |
| Resilience: opciones | Valores inválidos se toleraban | `ArgumentOutOfRangeException` en `Build()` | Corrige valores fuera de rango |
| Idempotency: clave | `IdempotencyKey` | `Type#len:scope#key`; entradas antiguas = miss | El handler se ejecuta una vez más tras el upgrade |
| Idempotency: payload | Misma clave devolvía la respuesta guardada | Misma clave con otro payload → `Conflict` / `IdempotencyConflictException` (`VerifyRequestFingerprint = false` lo desactiva) | Evita campos volátiles en el request |
| Idempotency: `Expiration == null` | Sin expiración | Expiración por defecto del store (24 h) | Define `Expiration` explícito si quieres otro valor |
| Idempotency/Caching | Respuestas `Result` fallidas se guardaban | No se guardan ni invalidan | Ninguna |
| ASP.NET Core | `Failure` (500) incluía `Result.Error` en `Detail` | Detalle genérico; `ResultHttpOptions.ExposeErrorDetails = true` lo restaura | Actívalo solo en desarrollo |
| Observability | Error de observer propagado como `AggregateException` | Aislado; visible en la `Activity`/`IMetricsCollector.RecordObserverError`. Mensaje de excepción oculto salvo `IncludeExceptionMessage = true` | Suscríbete al collector si necesitabas verlo |

## 3. Límites nuevos y defaults

| Opción | Default | Dónde |
|---|---|---|
| `InMemoryCacheOptions.MaxEntries` | 10 000 (antes 1 000) | Caching |
| `InMemoryCacheOptions.MaxKeyLength` / `MaxGroups` / `MaxKeysPerGroup` | 512 / 10 000 / 10 000 | Caching |
| `InMemoryCacheOptions.CleanupInterval` | 5 min | Caching |
| `CachingOptions.CoalescingWaitTimeout` | 30 s (`Timeout.InfiniteTimeSpan` permitido) | Caching |
| `IdempotencyOptions.MaxKeyLength` / `VerifyRequestFingerprint` | 256 / `true` | Idempotency |
| `InMemoryIdempotencyStoreOptions.MaxEntries` / `DefaultExpiration` | 10 000 / 24 h | Idempotency |
| `RateLimiterOptions.MaxPartitions` / `PartitionIdleTimeout` | 10 000 / 5 min | Resilience |
| `WithSharedState(key)` | tope 10 000 claves | Resilience |
| `IValiMediator.SendAll(requests, maxDegreeOfParallelism, ct)` | sin límite si no se indica | Core |
| `InMemoryDeadLetterQueue(maxEntries)` | 1 000 | Core |
| `ObservabilityOptions.IncludeExceptionMessage` | `false` | Observability |
| `ResultHttpOptions.ExposeErrorDetails` | `false` | AspNetCore |

Todos se validan (`ArgumentOutOfRangeException` si son inválidos) y se configuran desde el registro DI del paquete correspondiente.

## 4. Lista de comprobación

1. Sube `Vali-Mediator` a 3.0.0 y las 5 extensiones a 2.0.0.
2. Actualiza los `IPipelineBehavior` propios (`next(ct)`).
3. Revisa la tabla de la sección 2 contra tu uso real.
4. Ejecuta tus pruebas; si usas Idempotency, espera una re-ejecución por clave tras el deploy.
