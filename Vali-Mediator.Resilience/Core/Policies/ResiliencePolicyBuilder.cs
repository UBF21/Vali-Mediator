using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Registry;

namespace Vali_Mediator_Resilience.Core.Policies;

/// <summary>
/// Fluent builder for composing <see cref="ResiliencePolicy"/> instances.
/// Obtain one via <see cref="ResiliencePolicy.Create(string?)"/>.
/// </summary>
public sealed class ResiliencePolicyBuilder
{
    internal string? OperationKey { get; }

    private RetryOptions? _retry;
    private CircuitBreakerOptions? _circuitBreaker;
    private TimeoutOptions? _timeout;
    private BulkheadOptions? _bulkhead;
    private HedgeOptions? _hedge;
    private RateLimiterOptions? _rateLimiter;
    private ChaosOptions? _chaos;
    private ICircuitBreakerRegistry? _registry;

    internal ResiliencePolicyBuilder(string? operationKey)
    {
        OperationKey = operationKey;
    }

    // -----------------------------------------------------------------------
    // Policy configuration methods
    // -----------------------------------------------------------------------

    /// <summary>Adds a retry policy with the default options (3 retries, exponential jitter).</summary>
    public ResiliencePolicyBuilder Retry(int maxRetries = 3)
    {
        _retry = new RetryOptions { MaxRetries = maxRetries };
        return this;
    }

    /// <summary>Adds a retry policy with fine-grained configuration.</summary>
    public ResiliencePolicyBuilder Retry(Action<RetryOptions> configure)
    {
        _retry = new RetryOptions();
        configure(_retry);
        return this;
    }

    /// <summary>Adds a circuit breaker policy.</summary>
    public ResiliencePolicyBuilder CircuitBreaker(Action<CircuitBreakerOptions> configure)
    {
        _circuitBreaker = new CircuitBreakerOptions();
        if (OperationKey != null && string.IsNullOrEmpty(_circuitBreaker.CircuitKey))
            _circuitBreaker.CircuitKey = OperationKey;
        configure(_circuitBreaker);
        if (string.IsNullOrWhiteSpace(_circuitBreaker.CircuitKey))
            throw new InvalidOperationException("CircuitBreakerOptions.CircuitKey must be set.");
        return this;
    }

    /// <summary>Adds a timeout policy with the specified duration.</summary>
    public ResiliencePolicyBuilder Timeout(TimeSpan timeout)
    {
        _timeout = new TimeoutOptions { Timeout = timeout };
        return this;
    }

    /// <summary>Adds a timeout policy with fine-grained configuration.</summary>
    public ResiliencePolicyBuilder Timeout(Action<TimeoutOptions> configure)
    {
        _timeout = new TimeoutOptions();
        configure(_timeout);
        return this;
    }

    /// <summary>Adds a bulkhead (concurrency limiter) policy.</summary>
    public ResiliencePolicyBuilder Bulkhead(int maxConcurrent, int maxQueued = 0)
    {
        _bulkhead = new BulkheadOptions
        {
            MaxConcurrentCalls = maxConcurrent,
            MaxQueuedCalls = maxQueued
        };
        return this;
    }

    /// <summary>Adds a bulkhead policy with fine-grained configuration.</summary>
    public ResiliencePolicyBuilder Bulkhead(Action<BulkheadOptions> configure)
    {
        _bulkhead = new BulkheadOptions();
        configure(_bulkhead);
        return this;
    }

    /// <summary>
    /// Adds a hedge policy with default options (1 s delay, 1 additional hedge attempt).
    /// </summary>
    public ResiliencePolicyBuilder Hedge(TimeSpan hedgeDelay)
    {
        _hedge = new HedgeOptions { HedgeDelay = hedgeDelay };
        return this;
    }

    /// <summary>Adds a hedge policy with fine-grained configuration.</summary>
    public ResiliencePolicyBuilder Hedge(Action<HedgeOptions> configure)
    {
        _hedge = new HedgeOptions();
        configure(_hedge);
        return this;
    }

    /// <summary>
    /// Adds a rate-limiter policy with Token Bucket defaults (10 cap, 5 tokens/s).
    /// </summary>
    public ResiliencePolicyBuilder RateLimiter(int bucketCapacity, int tokensPerInterval = 5)
    {
        _rateLimiter = new RateLimiterOptions
        {
            BucketCapacity = bucketCapacity,
            TokensPerInterval = tokensPerInterval
        };
        return this;
    }

    /// <summary>Adds a rate-limiter policy with fine-grained configuration.</summary>
    public ResiliencePolicyBuilder RateLimiter(Action<RateLimiterOptions> configure)
    {
        _rateLimiter = new RateLimiterOptions();
        configure(_rateLimiter);
        return this;
    }

    /// <summary>
    /// Adds a chaos policy that injects faults with the given probability.
    /// </summary>
    public ResiliencePolicyBuilder Chaos(double injectionRate, Action<ChaosOptions>? configure = null)
    {
        _chaos = new ChaosOptions { InjectionRate = injectionRate };
        configure?.Invoke(_chaos);
        return this;
    }

    /// <summary>Adds a chaos policy with fine-grained configuration.</summary>
    public ResiliencePolicyBuilder Chaos(Action<ChaosOptions> configure)
    {
        _chaos = new ChaosOptions();
        configure(_chaos);
        return this;
    }

    /// <summary>
    /// Provides a custom <see cref="ICircuitBreakerRegistry"/> (e.g. for testing or DI injection).
    /// When not set, the built policy uses a private per-instance registry.
    /// </summary>
    public ResiliencePolicyBuilder UseRegistry(ICircuitBreakerRegistry registry)
    {
        _registry = registry;
        return this;
    }

    // -----------------------------------------------------------------------
    // Build
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds and returns the configured <see cref="ResiliencePolicy"/>.
    /// The fallback must be supplied per-call via <see cref="ResiliencePolicy.ExecuteAsync{T}(Func{CancellationToken, Task{T}}, FallbackOptions{T}?, CancellationToken)"/>
    /// because it is typed to the return value.
    /// </summary>
    public ResiliencePolicy Build()
    {
        var registry = _registry ?? new CircuitBreakerRegistry();
        return new ResiliencePolicy(OperationKey, _retry, _circuitBreaker, _timeout, _bulkhead, _hedge, _rateLimiter, _chaos, registry);
    }

    /// <summary>
    /// Builds a typed <see cref="ResiliencePolicy{T}"/> that includes a fallback policy for type <typeparamref name="T"/>.
    /// </summary>
    public ResiliencePolicy<T> Fallback<T>(Action<FallbackOptions<T>> configure)
    {
        var fallbackOptions = new FallbackOptions<T>();
        configure(fallbackOptions);
        var registry = _registry ?? new CircuitBreakerRegistry();
        return new ResiliencePolicy<T>(OperationKey, _retry, _circuitBreaker, _timeout, _bulkhead, _hedge, _rateLimiter, _chaos, fallbackOptions, registry);
    }

    internal RetryOptions? RetryOptions => _retry;
    internal CircuitBreakerOptions? CircuitBreakerOptions => _circuitBreaker;
    internal TimeoutOptions? TimeoutOptions => _timeout;
    internal BulkheadOptions? BulkheadOptions => _bulkhead;
    internal HedgeOptions? HedgeOptions => _hedge;
    internal RateLimiterOptions? RateLimiterOptions => _rateLimiter;
    internal ChaosOptions? ChaosOptions => _chaos;
    internal ICircuitBreakerRegistry EffectiveRegistry => _registry ?? new CircuitBreakerRegistry();
}
