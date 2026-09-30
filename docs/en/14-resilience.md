# Resilience

`Vali-Mediator.Resilience` is an optional package that adds resilience policies — retry, circuit breaker, timeout, bulkhead, hedge, rate limiter, chaos engineering, and fallback — to any request handler via the pipeline or directly on individual handlers.

---

## Installation

```bash
dotnet add package Vali-Mediator.Resilience
```

---

## DI Registration

Register the resilience behavior inside `AddValiMediator`. It inserts a pipeline behavior that applies policies declared on handlers implementing `IResilient`:

```csharp
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Resilience;

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddResilienceBehavior();
});
```

---

## ResiliencePolicy Builder

`ResiliencePolicy` exposes a fluent builder. Call `ResiliencePolicy.Create()` to start, chain one or more policies, then call `.Build()` to produce a configured policy pipeline.

```csharp
using Vali_Mediator.Resilience;

var policy = ResiliencePolicy.Create()
    .Retry(3)
    .Timeout(TimeSpan.FromSeconds(10))
    .Build();
```

An optional `operationKey` scopes circuit breaker and rate limiter state so that multiple independent operations do not share state:

```csharp
var policy = ResiliencePolicy.Create(operationKey: "PlaceOrder")
    .CircuitBreaker(opts =>
    {
        opts.FailureThreshold = 5;
        opts.BreakDuration    = TimeSpan.FromSeconds(30);
    })
    .Build();
```

---

## Policies

### Retry

Retries the operation on transient failures. Use the simple overload for defaults, or the options overload for full control.

**Simple overload**

```csharp
var policy = ResiliencePolicy.Create()
    .Retry(maxRetries: 3)
    .Build();
```

**Options overload**

```csharp
var policy = ResiliencePolicy.Create()
    .Retry(opts =>
    {
        opts.MaxRetries    = 3;
        opts.BackoffType   = BackoffType.ExponentialWithJitter;
        opts.RetryOnExceptions.Add(typeof(HttpRequestException));
        opts.RetryOnExceptions.Add(typeof(TimeoutException));
    })
    .Build();
```

| Option | Type | Default | Description |
|---|---|---|---|
| `MaxRetries` | `int` | `3` | Maximum retry attempts before the exception propagates |
| `BackoffType` | `BackoffType` | `Constant` | `Constant`, `Linear`, `Exponential`, `ExponentialWithJitter` |
| `RetryOnExceptions` | `List<Type>` | all exceptions | Restrict retries to specific exception types |

---

### Circuit Breaker

Tracks failures and opens the circuit after the failure threshold is reached. While open, all calls immediately throw `CircuitOpenException`. After `BreakDuration` elapses the circuit enters half-open state and allows a single probe call.

```csharp
var policy = ResiliencePolicy.Create(operationKey: "PaymentGateway")
    .CircuitBreaker(opts =>
    {
        opts.CircuitKey        = "PaymentGateway";
        opts.FailureThreshold  = 5;
        opts.BreakDuration     = TimeSpan.FromSeconds(30);
    })
    .Build();
```

| Option | Type | Default | Description |
|---|---|---|---|
| `CircuitKey` | `string?` | `null` (uses `operationKey`) | Shared key for circuit state — same key = shared breaker |
| `FailureThreshold` | `int` | `5` | Consecutive failures before opening |
| `BreakDuration` | `TimeSpan` | 30 seconds | How long the circuit stays open |

---

### Timeout

Cancels the operation if it does not complete within the specified duration.

**Simple overload**

```csharp
var policy = ResiliencePolicy.Create()
    .Timeout(TimeSpan.FromSeconds(10))
    .Build();
```

**Options overload**

```csharp
var policy = ResiliencePolicy.Create()
    .Timeout(opts =>
    {
        opts.Timeout  = TimeSpan.FromSeconds(10);
        opts.Strategy = TimeoutStrategy.Optimistic;
    })
    .Build();
```

| Option | Type | Default | Description |
|---|---|---|---|
| `Timeout` | `TimeSpan` | 30 seconds | Maximum operation duration |
| `Strategy` | `TimeoutStrategy` | `Pessimistic` | `Pessimistic` (hard cancel) or `Optimistic` (cooperative via `CancellationToken`) |

---

### Bulkhead

Limits the number of concurrent executions. Excess requests are queued up to `maxQueued`; beyond that `BulkheadRejectedException` is thrown immediately.

```csharp
var policy = ResiliencePolicy.Create()
    .Bulkhead(maxConcurrent: 10, maxQueued: 5)
    .Build();
```

| Parameter | Type | Description |
|---|---|---|
| `maxConcurrent` | `int` | Maximum simultaneous executions |
| `maxQueued` | `int` | Maximum requests waiting in queue |

---

### Hedge

Sends a duplicate (hedged) request after `hedgeDelay` if the original has not yet responded. The first response to arrive wins; the losing call is cancelled.

**Simple overload**

```csharp
var policy = ResiliencePolicy.Create()
    .Hedge(hedgeDelay: TimeSpan.FromMilliseconds(500))
    .Build();
```

**Options overload**

```csharp
var policy = ResiliencePolicy.Create()
    .Hedge(opts =>
    {
        opts.HedgeDelay        = TimeSpan.FromMilliseconds(500);
        opts.MaxHedgedAttempts = 2;
    })
    .Build();
```

| Option | Type | Default | Description |
|---|---|---|---|
| `HedgeDelay` | `TimeSpan` | 1 second | Delay before issuing the hedged attempt |
| `MaxHedgedAttempts` | `int` | `1` | Number of additional parallel attempts |

---

### Rate Limiter

Rejects calls that exceed the configured rate. `RateLimitExceededException` is thrown when the limit is hit.

**Simple overload** — token bucket with the given capacity:

```csharp
var policy = ResiliencePolicy.Create()
    .RateLimiter(bucketCapacity: 10)
    .Build();
```

**Options overload**

```csharp
var policy = ResiliencePolicy.Create()
    .RateLimiter(opts =>
    {
        opts.Algorithm   = RateLimiterAlgorithm.SlidingWindow;
        opts.PermitLimit = 100;
        opts.Window      = TimeSpan.FromSeconds(1);
    })
    .Build();
```

| Option | Type | Default | Description |
|---|---|---|---|
| `Algorithm` | `RateLimiterAlgorithm` | `TokenBucket` | `TokenBucket` or `SlidingWindow` |
| `BucketCapacity` / `TokensPerInterval` / `ReplenishmentInterval` | `int` / `int` / `TimeSpan` | `10` / `5` / 1 s | Token bucket. `TokensPerInterval = 0` disables replenishment; `BucketCapacity = 0` rejects every call |
| `PermitLimit` / `Window` | `int` / `TimeSpan` | `100` / 1 s | Sliding window. `PermitLimit = 0` rejects every call |
| `QueueTimeout` | `TimeSpan` | `0` (fail fast) | How long a caller waits for a permit before being rejected |
| `PartitionKeyResolver` | `Func<object, string>?` | `null` | One independent limiter per key (user, IP...) |
| `PartitionIdleTimeout` | `TimeSpan` | 5 min | Idle partitions are discarded (never before they have fully recovered) |
| `MaxPartitions` | `int` | `10 000` | Upper bound on live partitions; beyond it new keys share one overflow limiter |

`PartitionKeyResolver` receives the request. Through the mediator (`ResilienceBehavior`) that is automatic; when you call a policy directly use `policy.ExecuteForRequestAsync(request, ct => ...)` (plain `ExecuteAsync` throws `InvalidOperationException` because no request is available).

---|---|---|---|
| `Algorithm` | `RateLimiterAlgorithm` | `TokenBucket` | `TokenBucket`, `FixedWindow`, `SlidingWindow`, `Concurrency` |
| `PermitLimit` | `int` | `10` | Maximum permits in the window or bucket capacity |
| `Window` | `TimeSpan` | 1 second | Window duration for fixed/sliding window algorithms |

---

### Chaos

Injects faults or latency at a configurable rate. Intended for resilience testing; do not enable in production without deliberate control.

**Exception injection**

```csharp
var policy = ResiliencePolicy.Create()
    .Chaos(injectionRate: 0.1, opts =>
    {
        opts.ExceptionFactory = () => new Exception("chaos");
    })
    .Build();
```

**Latency injection**

```csharp
var policy = ResiliencePolicy.Create()
    .Chaos(opts =>
    {
        opts.InjectionRate    = 0.05;
        opts.LatencyInjection = TimeSpan.FromMilliseconds(200);
    })
    .Build();
```

| Option | Type | Description |
|---|---|---|
| `InjectionRate` | `double` | Probability `[0.0, 1.0]` that chaos is applied to any given call |
| `ExceptionFactory` | `Func<Exception>?` | Factory for the injected exception; `null` disables exception injection |
| `LatencyInjection` | `TimeSpan?` | Additional delay injected before the call; `null` disables latency injection |

---

### Fallback

Returns a substitute value or executes a fallback action when all other policies have been exhausted.

```csharp
var policy = ResiliencePolicy.Create<ProductDto>()
    .Retry(2)
    .Fallback<ProductDto>(opts =>
    {
        opts.FallbackValue = default;
    })
    .Build();
```

You can also provide a factory that receives the triggering exception:

```csharp
var policy = ResiliencePolicy.Create<ProductDto>()
    .Retry(2)
    .Fallback<ProductDto>(opts =>
    {
        opts.FallbackFactory = ex => new ProductDto { Name = "Unavailable" };
    })
    .Build();
```

---

## Direct Execution

Use the policy independently of the pipeline by calling `ExecuteAsync` directly:

**With return value**

```csharp
var result = await policy.ExecuteAsync<ProductDto>(
    ct => _httpClient.GetFromJsonAsync<ProductDto>("/products/1", ct),
    cancellationToken);
```

**Void (no return value)**

```csharp
await policy.ExecuteAsync(
    ct => _repository.SaveChangesAsync(ct),
    cancellationToken);
```

---

## IResilient — Handler-Level Policies

Implement `IResilient` on a handler to attach a dedicated policy to that handler without relying on the global pipeline behavior registration:

```csharp
using Vali_Mediator.Resilience;

public class GetProductQueryHandler
    : IRequestHandler<GetProductQuery, Result<ProductDto>>, IResilient
{
    public ResiliencePolicy ResiliencePolicy { get; } =
        ResiliencePolicy.Create(operationKey: "GetProduct")
            .Retry(opts =>
            {
                opts.MaxRetries  = 2;
                opts.BackoffType = BackoffType.ExponentialWithJitter;
            })
            .CircuitBreaker(opts =>
            {
                opts.FailureThreshold = 5;
                opts.BreakDuration    = TimeSpan.FromSeconds(20);
            })
            .Timeout(TimeSpan.FromSeconds(5))
            .Build();

    public async Task<Result<ProductDto>> Handle(
        GetProductQuery query, CancellationToken ct)
    {
        // handler implementation
    }
}
```

When `AddResilienceBehavior()` is registered, the pipeline behavior inspects whether the resolved handler implements `IResilient` and, if so, wraps the handler invocation with `handler.ResiliencePolicy`.

---

## IHasTimeout — Declarative Per-Request Timeout

`IHasTimeout` is a marker interface for `IRequest<TResponse>`. When a request implements it, `TimeoutBehavior` automatically enforces the declared timeout — no policy wiring required on the handler.

```csharp
using Vali_Mediator.Resilience;

public record GenerateReportQuery(int ReportId)
    : IRequest<Result<ReportDto>>, IHasTimeout
{
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
}
```

Register `TimeoutBehavior` alongside (or instead of) the full resilience behavior:

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddResilienceBehavior();   // includes TimeoutBehavior automatically
});
```

If only `IHasTimeout` support is needed and the full resilience package is too heavy, register `TimeoutBehavior` individually:

```csharp
config.AddRequestBehavior(typeof(TimeoutBehavior<,>));
```

---

## Resilience Exceptions

| Exception | Thrown when |
|---|---|
| `CircuitOpenException` | A call is made while the circuit breaker is open |
| `BulkheadRejectedException` | The bulkhead queue is full and the call cannot be accepted |
| `RateLimitExceededException` | The rate limit has been exceeded |
| `TimeoutException` | The operation did not complete within the configured timeout |

All resilience exceptions inherit from `ValiResilienceException`, which in turn inherits from `ValiMediatorException`.

```csharp
try
{
    var result = await mediator.Send(new GetProductQuery(id), ct);
}
catch (CircuitOpenException ex)
{
    // Service is temporarily unavailable; handle gracefully
}
catch (TimeoutException ex)
{
    // Operation exceeded its deadline
}
```

---

## Combining Policies

Policies are composed into a single pipeline. Use multiple chained calls on the builder:

```csharp
var policy = ResiliencePolicy.Create(operationKey: "ExternalApi")
    .Fallback<ApiResponse>(opts =>
    {
        opts.FallbackValue = ApiResponse.Empty;
    })
    .RateLimiter(opts =>
    {
        opts.Algorithm   = RateLimiterAlgorithm.SlidingWindow;
        opts.PermitLimit = 200;
        opts.Window      = TimeSpan.FromSeconds(1);
    })
    .Timeout(TimeSpan.FromSeconds(10))
    .CircuitBreaker(opts =>
    {
        opts.FailureThreshold = 10;
        opts.BreakDuration    = TimeSpan.FromMinutes(1);
    })
    .Bulkhead(maxConcurrent: 20, maxQueued: 10)
    .Retry(opts =>
    {
        opts.MaxRetries    = 3;
        opts.BackoffType   = BackoffType.ExponentialWithJitter;
    })
    .Hedge(opts =>
    {
        opts.HedgeDelay        = TimeSpan.FromMilliseconds(300);
        opts.MaxHedgedAttempts = 1;
    })
    .Build();
```

---

## Policy Execution Order

Policies are applied from outermost to innermost. The following is the execution order when all policies are combined:

```
Fallback
  └── Chaos
        └── RateLimiter
              └── Retry
                    └── Timeout
                          └── Circuit Breaker
                                └── Bulkhead
                                      └── Hedge
                                            └── delegate (handler)
```

This means:
- **Fallback** catches any exception that escapes the entire inner stack.
- **Chaos** may inject a fault before the real call is attempted.
- **RateLimiter** takes one permit per logical call: rejections are never retried and never count as circuit-breaker failures.
- **Retry** re-attempts the whole Timeout → Circuit Breaker → Bulkhead → Hedge chain on failure.
- **Timeout** is applied **per attempt** (each retry gets a fresh timeout).
- **Circuit Breaker** short-circuits when cumulative failures exceed the threshold; an open circuit stops the retries. Caller cancellation and bulkhead rejections are not counted as failures.
- **Bulkhead** limits concurrency at the point of actual execution. Up to `MaxQueuedCalls` callers wait for a slot (forever when `QueueTimeout` is infinite); the rest are rejected.
- **Hedge** issues a duplicate parallel call if the primary is slow.

---

## Stateful Policies, Limits and Validation

**Policies are resolved per request.** `ResilienceBehavior` calls `GetPolicy(request)` on every request. Bulkhead slots,
rate-limiter buckets and circuit breakers live inside the policy instance, so a factory that builds a *new* policy
each time silently starts every request with a fresh, empty limiter. Either return the same instance every time, or
give the policy a fixed name with `WithSharedState`:

```csharp
services.AddResiliencePolicy<LoginCommand>(req =>
    ResiliencePolicy.Create("login")
        .Bulkhead(maxConcurrent: 20)
        .RateLimiter(o => { o.BucketCapacity = 100; o.TokensPerInterval = 50; })
        .WithSharedState("login")   // bulkhead, limiter and circuit breaker are shared by name
        .Build());
```

Use a small set of fixed names, never request data: the number of distinct keys is capped at 10 000, and the first
policy to use a name defines its limits.

**Validation.** `Build()` throws `ArgumentOutOfRangeException` for out-of-range options (negative retries, zero
timeout, zero concurrency, failure rate outside 0–1, non-positive replenishment interval, `MaxPartitions < 1`…)
instead of failing later at run time.

**Rate limiter.** `TokensPerInterval = 0` disables replenishment and `BucketCapacity` / `PermitLimit = 0` reject every
call. `MaxPartitions` (default 10 000) bounds the live partitions: beyond it, new keys share one overflow limiter, so a
flood of client-chosen keys cannot exhaust memory. A caller with `QueueTimeout > 0` sleeps until the next permit can
exist instead of polling.

**Fallback on a failed result.** `FallbackOptions.FallbackOnResultPredicate` replaces a non-throwing failed result
(for example `Result.Fail`); `OnFallback` receives a `null` exception in that case.

**Hedge.** The first acceptable result is returned immediately. The losing attempts are cancelled but not awaited, so
an operation that ignores its `CancellationToken` keeps running in the background.

**Circuit breaker.** A call's outcome only counts if the circuit is still in the state it was admitted in, so slow
calls that finish after a transition cannot close or re-open it. A HalfOpen probe that never reports back is replaced
after `BreakDuration`.

**Chaos.** By default chaos sits outside Retry, Timeout and the circuit breaker: it exercises the Fallback and the
caller. Set `ChaosOptions.InjectPerAttempt = true` to inject the fault on every attempt, so retries, timeouts and the
circuit breaker react to it.

---

## Next Steps

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — How the resilience behavior hooks into the pipeline
- **[Result](10-result.md)** — Combining resilience with the Result pattern
- **[Caching](15-caching.md)** — Response caching for request handlers
