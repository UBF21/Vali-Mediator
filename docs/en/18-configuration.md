# Configuration and limits

Every Vali-Mediator limit is changed when you **register** the library, through an `Action<TOptions>`. The library does not depend on `Microsoft.Extensions.Options`: to read from `appsettings.json`, bind the options object with `Bind`.

Values are validated **when they are set** (in the setter): an invalid value throws `ArgumentOutOfRangeException` naming the property at startup, not on the first request. Per-policy Resilience options are validated in `Build()`.

## Binding from `appsettings.json`

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

Resilience policies are configured the same way inside the builder: `ResiliencePolicy.Create().Retry(o => cfg.GetSection("Retry").Bind(o))`.

## Core (`Vali-Mediator`)

| Option | Registration | Default | Valid range | Effect |
|---|---|---|---|---|
| `SendAllMaxDegreeOfParallelism` | `AddValiMediator(c => ...)` | `null` (all at once) | `null` or `>= 1` | Requests in flight for `SendAll(requests)` when no explicit limit is passed. `SendAll(requests, max)` overrides it. |
| DLQ `maxEntries` | `AddInMemoryDeadLetterQueue(maxEntries)` | `1000` | `>= 1` | Failures retained; the oldest are dropped first. |

## Caching

| Option | Registration | Default | Valid range | Effect |
|---|---|---|---|---|
| `MaxEntries` | `AddInMemoryCacheStore(o => ...)` | `10000` | `>= 1` | Store capacity; the least-recently-used entry is evicted when full. |
| `MaxKeyLength` | same | `512` | `>= 1` | Longer keys (and group names) are not stored; the handler still runs. |
| `MaxGroups` | same | `10000` | `>= 1` | Distinct invalidation groups; beyond it the entry is not stored (never left uninvalidatable). |
| `MaxKeysPerGroup` | same | `10000` | `>= 1` | Keys per group; same rule. |
| `CleanupInterval` | same | `5 min` | `> 0` | At most how often expired entries are swept (lazily, on write). |
| `TimeProvider` | same | `TimeProvider.System` | not null | Clock (tests). Not bindable from configuration. |
| `CoalescingWaitTimeout` | `AddCachingOptions(o => ...)` | `30 s` | `> 0` or `Timeout.InfiniteTimeSpan` | Longest wait of requests sharing a miss before running the handler themselves. |

## Idempotency

| Option | Registration | Default | Valid range | Effect |
|---|---|---|---|---|
| `MaxKeyLength` | `AddIdempotencyOptions(o => ...)` | `256` | `>= 1` | Max length of `IdempotencyKey` and `IdempotencyScope` (each); longer throws `ArgumentException`. |
| `VerifyRequestFingerprint` | same | `true` | `bool` | Compares the request SHA-256; the same key with another payload is a conflict. Disable it with volatile fields. |
| `MaxEntries` | `AddInMemoryIdempotencyStore(o => ...)` | `10000` | `>= 1` | Store capacity; the oldest entries are evicted. |
| `DefaultExpiration` | same | `24 h` | `null` or `> 0` | Expiry for entries stored without one. `null` keeps them until evicted. |

## Observability

| Option | Registration | Default | Valid range | Effect |
|---|---|---|---|---|
| `IncludeExceptionMessage` | `AddObservability(o => ...)` | `false` | `bool` | When `false`, telemetry and `ConsoleLoggingObserver` expose only the exception type, not its message. |

## AspNetCore

| Option | Registration | Default | Valid range | Effect |
|---|---|---|---|---|
| `ExposeErrorDetails` | `AddResultHttpOptions(o => ...)` | `false` | `bool` | When `false`, 500 responses carry a generic detail. 4xx always show their message. Applied by `result.ToActionResult(HttpContext)` / `result.ToHttpResult(HttpContext)`; without registration the default applies. |

## Resilience

| Option | Registration | Default | Valid range | Effect |
|---|---|---|---|---|
| `MaxSharedStates` | `AddResilienceOptions(o => ...)` | `10000` | `>= 1` | Distinct `WithSharedState` keys; beyond it `InvalidOperationException` is thrown. Process-wide; the last call wins. |
| `Retry.MaxRetries` | `.Retry(o => ...)` | `3` | `>= 0` | Retries. |
| `Retry.InitialDelay` / `MaxDelay` | same | `200 ms` / `30 s` | `>= 0` | Backoff; the delay is clamped to `MaxDelay` before building the `TimeSpan`. |
| `Retry.Multiplier` | same | `2.0` | finite `> 0` | Exponential backoff factor. |
| `CircuitBreaker.FailureThreshold` | `.CircuitBreaker(o => ...)` | `5` | `>= 1` | Failures to open. |
| `CircuitBreaker.FailureRateThreshold` | same | `0` | `0..1` | Failure rate to open (with `MinimumThroughput`). |
| `CircuitBreaker.MinimumThroughput` | same | `10` | `>= 1` | Minimum calls before the rate is evaluated. |
| `CircuitBreaker.SamplingDuration` / `BreakDuration` | same | `60 s` / `30 s` | `> 0` | Sampling window and open time. |
| `CircuitBreaker.HalfOpenMaxAttempts` | same | `1` | `>= 1` | Simultaneous probes in HalfOpen. |
| `Timeout.Timeout` | `.Timeout(o => ...)` | `30 s` | `> 0` | Per-attempt timeout. |
| `Bulkhead.MaxConcurrentCalls` | `.Bulkhead(o => ...)` | `10` | `>= 1` | Concurrent calls. |
| `Bulkhead.MaxQueuedCalls` | same | `0` | `>= 0` | Wait queue; `0` rejects immediately. |
| `Bulkhead.QueueTimeout` | same | infinite | `>= 0` or infinite | Max time waiting in the queue. |
| `Hedge.HedgeDelay` / `MaxHedgedAttempts` | `.Hedge(o => ...)` | `1 s` / `1` | `>= 0` | Delay and number of extra attempts. |
| `Chaos.InjectionRate` | `.Chaos(o => ...)` | `0` | `0..1` | Injection probability. |
| `RateLimiter.BucketCapacity` / `TokensPerInterval` / `ReplenishmentInterval` | `.RateLimiter(o => ...)` | `10` / `5` / `1 s` | `>= 0` / `>= 0` / `> 0` | Token bucket; capacity `0` rejects everything, `0` tokens disables refill. |
| `RateLimiter.PermitLimit` / `Window` | same (`SlidingWindow`) | `100` / `1 s` | `>= 0` / `> 0` | Sliding window. |
| `RateLimiter.QueueTimeout` | same | `0` | `>= 0` | Wait for a permit. |
| `RateLimiter.MaxPartitions` | same | `10000` | `>= 1` | Active partitions; extra keys share an overflow limiter. |
| `RateLimiter.PartitionIdleTimeout` | same | `5 min` | `>= 0` | Idle time after which a partition is released. |

Boundary values (`1`, `int.MaxValue`, `0` where allowed) and invalid values are covered by parameterized tests in each `*.Tests` project.
