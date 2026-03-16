# Vali-Mediator.Resilience

A **zero-dependency** resilience library for .NET 7/8/9, built to complement `Vali-Mediator` — or be used entirely on its own.

No Polly. No extra frameworks. Just a clean, fluent API with production-grade policies.

---

## Features

| Policy | Highlights |
|--------|-----------|
| **Retry** | Fixed / Linear / Exponential / Exponential+Jitter / Custom backoff · per-exception, per-`ErrorType`, per-result predicates · `OnRetry` callback |
| **Circuit Breaker** | Sliding-window state machine (Closed → Open → HalfOpen) · absolute threshold _or_ rate-based threshold · `OnOpen/OnClose/OnHalfOpen` callbacks · keyed registry for shared state |
| **Timeout** | Optimistic (CancellationToken) and Pessimistic (Task.WhenAny) strategies · `OnTimeout` callback |
| **Fallback** | Typed fallback value or async factory · conditional activation · `OnFallback` callback |
| **Bulkhead** | Semaphore-based concurrency limiter · optional queue with configurable timeout · `OnRejected` callback |
| **Presets** | `ForExternalApi`, `ForDatabase`, `ForCritical`, `NoResilience` |
| **Vali-Mediator** | `IResilient` + `ResilienceBehavior<TRequest,TResponse>` auto-applies policy from the request |

---

## Installation

```bash
dotnet add package Vali-Mediator.Resilience
```

---

## Standalone Usage

### Retry with exponential jitter

```csharp
var policy = ResiliencePolicy.Create("payment-gateway")
    .Retry(options =>
    {
        options.MaxRetries = 3;
        options.BackoffType = BackoffType.ExponentialWithJitter;
        options.InitialDelay = TimeSpan.FromMilliseconds(200);
        options.MaxDelay = TimeSpan.FromSeconds(5);
        options.OnRetry = (ctx, delay) =>
        {
            Console.WriteLine($"Retry {ctx.AttemptNumber}, waiting {delay.TotalMilliseconds:0}ms");
            return Task.CompletedTask;
        };
    })
    .Build();

string html = await policy.ExecuteAsync(ct => httpClient.GetStringAsync(url, ct));
```

### Circuit Breaker + Timeout + Fallback

```csharp
var policy = ResiliencePolicy.Create("inventory-service")
    .CircuitBreaker(options =>
    {
        options.CircuitKey = "inventory-service";
        options.FailureThreshold = 5;
        options.SamplingDuration = TimeSpan.FromSeconds(30);
        options.BreakDuration = TimeSpan.FromSeconds(60);
        options.OnOpen = (ctx, ex) =>
        {
            logger.LogWarning("Circuit opened: {Key}", ctx.OperationKey);
            return Task.CompletedTask;
        };
    })
    .Timeout(TimeSpan.FromSeconds(10))
    .Fallback<InventoryDto>(options =>
    {
        options.FallbackValue = InventoryDto.Empty;
        options.OnFallback = (ctx, ex) =>
        {
            logger.LogWarning(ex, "Falling back on {Key}", ctx.OperationKey);
            return Task.CompletedTask;
        };
    })
    .ExecuteAsync(ct => inventoryClient.GetAsync(productId, ct));
```

### Rate-based circuit breaker

```csharp
var policy = ResiliencePolicy.Create("payments")
    .CircuitBreaker(options =>
    {
        options.CircuitKey = "payments";
        options.FailureRateThreshold = 0.5;   // open when ≥50% of calls fail
        options.MinimumThroughput = 20;        // need at least 20 calls to evaluate
        options.SamplingDuration = TimeSpan.FromSeconds(60);
        options.BreakDuration = TimeSpan.FromSeconds(30);
    })
    .Build();
```

### Bulkhead

```csharp
var policy = ResiliencePolicy.Create("db")
    .Bulkhead(options =>
    {
        options.MaxConcurrentCalls = 20;
        options.MaxQueuedCalls = 5;
        options.QueueTimeout = TimeSpan.FromSeconds(3);
        options.OnRejected = ctx =>
        {
            metrics.IncrementCounter("db.rejected");
            return Task.CompletedTask;
        };
    })
    .Build();
```

### Void operation

```csharp
await policy.ExecuteAsync(ct => cache.FlushAsync(ct));
```

---

## Presets

```csharp
// HTTP API calls — Retry × 3 (jitter) + CB(5 fails/30s) + Timeout 15s
var policy = ResiliencePolicy.Presets.ForExternalApi("payment-gateway");

// Database — Retry × 2 (linear) + CB(3 fails/20s) + Timeout 30s + Bulkhead 20
var policy = ResiliencePolicy.Presets.ForDatabase("sqlserver");

// Critical paths — Retry × 5 (jitter) + Rate CB(50%/20 calls/60s) + Timeout 20s + Bulkhead 5
var policy = ResiliencePolicy.Presets.ForCritical("auth");

// No resilience (pass-through)
var policy = ResiliencePolicy.Presets.NoResilience();
```

---

## Vali-Mediator Integration

### Setup

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddResilienceBehavior();  // registers ResilienceBehavior<,>
});

// Optional: shared circuit state across all policies (singleton registry)
builder.Services.AddResilienceRegistry();
```

### Declare policy on the request

```csharp
public class CallPaymentGatewayCommand : IRequest<Result<PaymentDto>>, IResilient
{
    public string OrderId { get; init; } = string.Empty;

    // Evaluated once per dispatch; cache as static for best performance
    private static readonly ResiliencePolicy _policy = ResiliencePolicy
        .Create("payment-gateway")
        .Retry(options =>
        {
            options.MaxRetries = 3;
            options.BackoffType = BackoffType.ExponentialWithJitter;
            options.InitialDelay = TimeSpan.FromMilliseconds(200);
            options.MaxDelay = TimeSpan.FromSeconds(5);
            options.RetryOnErrorTypes.Add(ErrorType.Failure);
        })
        .CircuitBreaker(options =>
        {
            options.CircuitKey = "payment-gateway";
            options.FailureThreshold = 5;
            options.SamplingDuration = TimeSpan.FromSeconds(30);
            options.BreakDuration = TimeSpan.FromSeconds(60);
        })
        .Timeout(TimeSpan.FromSeconds(10))
        .Build();

    public ResiliencePolicy Policy => _policy;
}
```

The `ResilienceBehavior` intercepts the request in the Vali-Mediator pipeline and wraps handler execution in the declared policy — no code changes needed in the handler.

---

## Exception Types

| Exception | Thrown when |
|-----------|-------------|
| `CircuitOpenException` | A request arrives while the circuit is `Open`. Contains `CircuitKey` and `RetryAfter`. |
| `BulkheadRejectedException` | Concurrency slots and queue are both full. Contains limits. |
| `TimeoutException` | Operation exceeded the configured `Timeout`. |

---

## ResilienceContext

All callbacks receive a `ResilienceContext`:

```csharp
options.OnRetry = (ctx, delay) =>
{
    ctx.AttemptNumber   // 0-based attempt index
    ctx.ElapsedTime     // time since operation started
    ctx.LastException   // exception from last attempt
    ctx.OperationKey    // key from ResiliencePolicy.Create("key")
    ctx.Properties      // Dictionary<string,object?> for custom data
    ctx.CancellationToken
    return Task.CompletedTask;
};
```

---

## Circuit Breaker Registry

By default each `ResiliencePolicy` instance owns its own circuit state. To share state across instances (e.g. multiple command types hitting the same downstream service), register the singleton registry:

```csharp
// DI registration (recommended)
services.AddResilienceRegistry();

// Standalone — pass the registry when building
var registry = new CircuitBreakerRegistry();
var policy = ResiliencePolicy.Create("shared-circuit")
    .CircuitBreaker(o => { o.CircuitKey = "shared-circuit"; ... })
    .UseRegistry(registry)
    .Build();

// Inspect or reset
CircuitState? state = registry.GetState("shared-circuit");
registry.Reset("shared-circuit");
```

---

## Backoff Types

| Type | Formula |
|------|---------|
| `Fixed` | `InitialDelay` every attempt |
| `Linear` | `InitialDelay × attempt` |
| `Exponential` | `InitialDelay × Multiplier^attempt` |
| `ExponentialWithJitter` | Exponential ± 20% random jitter (thundering-herd prevention) |
| `Custom` | `RetryOptions.CustomDelayFactory(attemptIndex)` |

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
