# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Vali-Mediator is a lightweight .NET library implementing the Mediator pattern with CQRS support. It targets .NET 7.0, 8.0, and 9.0 and depends only on `Microsoft.Extensions.DependencyInjection.Abstractions`. Published to NuGet as `Vali-Mediator` (current version: **2.0.0**).

### Projects in the Solution

| Project | Description |
|---------|-------------|
| `Vali-Mediator` | Core library — mediator, result pattern, pipeline |
| `Vali-Mediator.AspNetCore` | ASP.NET Core integration — maps `Result<T>`/`Result` to HTTP responses |
| `Vali-Mediator.Resilience` | Resilience policies — Retry, CircuitBreaker, Timeout, Bulkhead, Hedge, RateLimiter, Chaos, Fallback |
| `Vali-Mediator.Caching` | Pipeline caching — `ICacheable`, `IInvalidatesCache`, `ICacheStore` |
| `Vali-Mediator.Observability` | Telemetry — `ActivitySource` (OpenTelemetry-compatible), `IMetricsCollector`, `IRequestObserver` |
| `Vali-Mediator.Idempotency` | Idempotent request handling — `IIdempotent`, `IIdempotencyStore` |
| `Vali-Mediator.Tests` | Core unit tests (60 tests) |
| `Vali-Mediator.Resilience.Tests` | Resilience unit tests (87 tests) |
| `Vali-Mediator.Caching.Tests` | Caching unit tests (24 tests) |
| `Vali-Mediator.Observability.Tests` | Observability unit tests (18 tests) |
| `Vali-Mediator.Idempotency.Tests` | Idempotency unit tests (17 tests) |
| `Vali-Mediator.Benchmarks` | BenchmarkDotNet benchmarks |

## Build Commands

```bash
# Build the solution (targets net7.0, net8.0, net9.0)
/usr/local/share/dotnet/x64/dotnet build Vali-Mediator.sln

# Build release
/usr/local/share/dotnet/x64/dotnet build -c Release

# Run tests
/usr/local/share/dotnet/x64/dotnet test Vali-Mediator.Tests

# Pack NuGet package (core)
/usr/local/share/dotnet/x64/dotnet pack Vali-Mediator/Vali-Mediator.csproj -c Release

# Pack NuGet package (AspNetCore integration)
/usr/local/share/dotnet/x64/dotnet pack Vali-Mediator.AspNetCore/Vali-Mediator.AspNetCore.csproj -c Release
```

**Note:** `dotnet` is not on PATH — use the full path `/usr/local/share/dotnet/x64/dotnet`.

**C# version constraint:** The project targets net7.0 (C# 11). Avoid collection expressions (`[]`), primary constructors on classes, and other C# 12+ features.

## Architecture

All source lives in `Vali-Mediator/Core/` organized by feature:

### Dispatch Hierarchy

```
IRequest<TResponse>   → IRequestHandler<TRequest, TResponse>         (1 handler, returns TResponse)
IRequest              → IRequest<Unit>                                (shortcut for void requests)
IRequestHandler<T>    → IRequestHandler<T, Unit>                     (shorthand for void handlers)
INotification         → INotificationHandler<T>                      (N handlers, Priority-ordered)
IFireAndForget        → IFireAndForgetHandler<T>                     (1 handler, no response)
IStreamRequest<T>     → IStreamRequestHandler<TRequest, TResponse>   (1 handler, IAsyncEnumerable)

IDispatch  ← INotification, IFireAndForget   (IRequest does NOT inherit IDispatch)
```

### Pipeline Execution Order

**PreProcessors → PipelineBehaviors (outer-to-inner) → Handler → PostProcessors**

- First registered behavior = outermost (executes first). Implemented via `Enumerable.Reverse` + closure chain.
- `IPipelineBehavior<TRequest, TResponse>` — for IRequest
- `IPipelineBehavior<TDispatch>` — for INotification and IFireAndForget
- Pre/PostProcessors return `Task` (async). Auto-discovered from assembly scan.
- Streaming (`CreateStream`) bypasses the pipeline entirely.

### Key Features (v2.0.0)

| Feature | API |
|---------|-----|
| Result pattern (generic) | `Result<T>.Ok(value)` / `Result<T>.Fail(msg, ErrorType)` / `Result<T>.Fail(errorsDict, ErrorType)` |
| Result pattern (non-generic) | `Result.Ok()` / `Result.Fail(msg, ErrorType)` — for void-returning handlers |
| Result functional ops | `Map`, `Bind`, `MapAsync`, `BindAsync`, `Tap`, `OnFailure`, `Match` on both `Result` and `Result<T>` |
| Structured validation errors | `Result<T>.ValidationErrors` — `IReadOnlyDictionary<string, IReadOnlyList<string>>` |
| Shorthand void handler | `IRequestHandler<TRequest>` — implements `IRequestHandler<TRequest, Unit>` |
| Parallel notifications | `mediator.Publish(n, PublishStrategy.Parallel)` |
| Resilient parallel | `mediator.Publish(n, PublishStrategy.ResilientParallel)` — all handlers run even if some fail |
| SendOrDefault | `mediator.SendOrDefault(request)` → `default` if no handler |
| SendAll | `mediator.SendAll<T>(requests)` → `Task<T[]>` via `Task.WhenAll` |
| Streaming | `mediator.CreateStream(streamRequest)` → `IAsyncEnumerable<T>` |
| Typed exceptions | `HandlerNotFoundException : ValiMediatorException` |
| Lifetime control | `RegisterServicesFromAssemblyContaining<T>()` / `RegisterServicesFromAssembly(assembly, ServiceLifetime)` |
| Behavior registration | `config.AddRequestBehavior<T>()` / `config.AddDispatchBehavior<T>()` |
| Auto-discovery | Processors discovered automatically from assembly scan |
| Declarative timeout | `IHasTimeout` on `IRequest<T>` + `services.AddTimeoutBehavior()` |
| Notification filter | `INotificationFilter<TNotification>` on handler — `ShouldHandle()` skips when false |
| Dead letter queue | `IDeadLetterQueue` / `services.AddInMemoryDeadLetterQueue()` — captures `ResilientParallel` failures |
| IResult interface | `Result<T>` and `Result` both implement `IResult` — used by resilience/circuit breaker |

### Key Files

| File | Role |
|------|------|
| `Core/General/Mediator/IValiMediator.cs` | Public contract |
| `Core/General/Mediator/ValiMediator.cs` | Implementation — `ExecuteRequestPipeline`, `ExecuteNotificationHandler`, `ExecuteFireAndForgetPipeline` private helpers |
| `Core/General/Cache/ReflectionCache.cs` | `ConcurrentDictionary<(Type,string), MethodInfo>` — avoids repeated `GetMethod` calls |
| `Core/General/Extension/ValiMediatorExtension.cs` | `AddValiMediator()` — scans per-assembly with lifetime, auto-discovers processors |
| `Core/General/Extension/ValiMediatorConfiguration.cs` | Fluent config — `List<(Type, Type, ServiceLifetime)>` for all registrations |
| `Core/Result/Result.cs` | `readonly struct Result<T>` with functional methods |
| `Core/Result/ResultVoid.cs` | `readonly struct Result` — non-generic void result |
| `Core/Result/ErrorType.cs` | `ErrorType` enum: None, Validation, NotFound, Conflict, Unauthorized, Forbidden, Failure |
| `Core/General/Exceptions/` | `ValiMediatorException` base + `HandlerNotFoundException` |
| `Core/Streaming/` | `IStreamRequest<T>`, `IStreamRequestHandler<TRequest, TResponse>` |

### DI Registration Pattern

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    // Or with explicit assembly:
    config.RegisterServicesFromAssembly(typeof(Program).Assembly);
    // Optionally override lifetime (default: Scoped)
    config.RegisterServicesFromAssembly(typeof(Infrastructure.Marker).Assembly, ServiceLifetime.Transient);
    // Behaviors — first = outermost
    config.AddRequestBehavior<LoggingBehavior<MyRequest, MyResponse>>();
    config.AddDispatchBehavior<NotificationLoggingBehavior<MyNotification>>();
    // Or using open-generic:
    config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
});
```

### Vali-Mediator.AspNetCore Package

Maps `Result<T>` and `Result` to HTTP responses:

```csharp
using Vali_Mediator.AspNetCore;

// MVC Controller
public async Task<IActionResult> PlaceOrder(PlaceOrderCommand cmd)
{
    Result<string> result = await _mediator.Send(cmd);
    return result.ToActionResult(); // 200, 400, 404, 409, 401, 403, 500
}

// Minimal API
app.MapPost("/orders", async (PlaceOrderCommand cmd, IValiMediator m) =>
{
    Result<string> result = await m.Send(cmd);
    return result.ToHttpResult(); // IResult
});
```

| ErrorType | IActionResult | IResult |
|-----------|--------------|---------|
| None (success on `Result<T>`) | 200 OkObjectResult | 200 Ok |
| None (success on `Result`) | 204 NoContentResult | 204 NoContent |
| Validation | 400 BadRequestObjectResult / ValidationProblemDetails | 400 ValidationProblem |
| NotFound | 404 NotFoundObjectResult | 404 NotFound |
| Conflict | 409 ConflictObjectResult | 409 Conflict |
| Unauthorized | 401 UnauthorizedObjectResult | 401 Unauthorized |
| Forbidden | 403 ObjectResult | 403 StatusCode |
| Failure | 500 ObjectResult | 500 Problem |

### Vali-Mediator.Resilience Package

Fluent `ResiliencePolicyBuilder` via `ResiliencePolicy.Create()`. Execution order: **Fallback → Chaos → RateLimiter → Timeout → Circuit Breaker → Bulkhead → Retry → Hedge → delegate**.

| Policy | Builder method | Key options |
|--------|---------------|-------------|
| Retry | `.Retry(n)` / `.Retry(opts=>{})` | `MaxRetries`, `BackoffType`, `RetryOnExceptions`, `RetryOnErrorTypes` |
| Circuit Breaker | `.CircuitBreaker(opts=>{})` | `CircuitKey`, `FailureThreshold`, `BreakDuration` |
| Timeout | `.Timeout(ts)` / `.Timeout(opts=>{})` | `Strategy` (Optimistic/Pessimistic) |
| Bulkhead | `.Bulkhead(maxConcurrent, maxQueued)` | `QueueTimeout`, `OnRejected` |
| Hedge | `.Hedge(delay)` / `.Hedge(opts=>{})` | `HedgeDelay`, `MaxHedgedAttempts`, `ShouldHedgeOnResult/Exception`, `OnHedge` |
| Rate Limiter | `.RateLimiter(cap)` / `.RateLimiter(opts=>{})` | `Algorithm` (TokenBucket/SlidingWindow), `BucketCapacity`, `PermitLimit`, `Window` |
| Chaos | `.Chaos(rate)` / `.Chaos(opts=>{})` | `InjectionRate`, `ExceptionFactory`, `LatencyInjection`, `ResultFactory`, `Random` |
| Fallback | `.Fallback<T>(opts=>{})` | `FallbackValue`, `FallbackFactory`, `OnFallback` |

Integration: implement `IResilient` on a handler to apply a `ResiliencePolicy` at the handler level via `ResilienceBehavior`.

### Vali-Mediator.Caching Package

- `ICacheable` on `IRequest<T>`: `CacheKey`, `AbsoluteExpiration`, `SlidingExpiration`, `CacheGroup`, `BypassCache`, `Order` (CacheOrder enum)
- `IInvalidatesCache` on any request: `InvalidatedKeys`, `InvalidatedGroups`
- `ICacheStore` abstraction — replace with Redis/distributed via `services.AddCacheStore<T>()`
- DI: `config.AddCachingBehavior()` + `services.AddInMemoryCacheStore()`

### Vali-Mediator.Observability Package

- `ValiMediatorDiagnostics.ActivitySource` — source name `"Vali-Mediator"` v2.0.0, OpenTelemetry-compatible
- `IMetricsCollector` — `RecordRequest(name, duration, success, errorType?)`; built-ins: `NoOpMetricsCollector`, `ConsoleMetricsCollector`
- `IRequestObserver` — `OnStarted/OnCompleted/OnFailed(ObservabilityContext, ct)`; multiple observers supported
- DI: `services.AddObservability()` + `config.AddObservabilityBehavior()`

### Vali-Mediator.Idempotency Package

- `IIdempotent` on `IRequest<T>`: `IdempotencyKey`, `Expiration`
- `IIdempotencyStore` — `FindAsync/StoreAsync/RemoveAsync/ExistsAsync`; default: `InMemoryIdempotencyStore`
- `IIdempotencySerializer` — default: `JsonIdempotencySerializer`
- DI: `config.AddIdempotencyBehavior()` + `services.AddInMemoryIdempotencyStore()`

### Implementation Details

- Namespace root: `Vali_Mediator` (underscore, not hyphen); Resilience: `Vali_Mediator_Resilience`
- `IRequest<TResponse>` does NOT implement `IDispatch` — kept separate by design
- `GetServices` returns `IEnumerable<object?>` — use `List<object?>` in private methods
- `object?[]` required for post-processor invocation when TResponse is nullable
- `IResult` interface implemented by both `Result<T>` and `Result` — used by resilience circuit breaker and retry
