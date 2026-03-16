using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Pipeline;
using Vali_Mediator_Resilience.Core.Presets;
using Vali_Mediator_Resilience.Core.Registry;

namespace Vali_Mediator_Resilience.Core.Policies;

/// <summary>
/// A composed resilience policy built via <see cref="ResiliencePolicyBuilder"/>.
/// Supports both typed (<c>ExecuteAsync&lt;T&gt;</c>) and void (<c>ExecuteAsync</c>) execution.
/// </summary>
public sealed class ResiliencePolicy
{
    private readonly ResiliencePipeline _pipeline;
    internal readonly string? _operationKey;

    internal ResiliencePolicy(
        string? operationKey,
        RetryOptions? retry,
        CircuitBreakerOptions? circuitBreaker,
        TimeoutOptions? timeout,
        BulkheadOptions? bulkhead,
        HedgeOptions? hedge,
        RateLimiterOptions? rateLimiter,
        ChaosOptions? chaos,
        ICircuitBreakerRegistry registry)
    {
        _operationKey = operationKey;
        _pipeline = new ResiliencePipeline(retry, circuitBreaker, timeout, bulkhead, hedge, rateLimiter, chaos, registry);
    }

    // -----------------------------------------------------------------------
    // Factory
    // -----------------------------------------------------------------------

    /// <summary>Starts building a new <see cref="ResiliencePolicy"/>.</summary>
    /// <param name="operationKey">Optional key used for circuit breaker naming and logging.</param>
    public static ResiliencePolicyBuilder Create(string? operationKey = null)
        => new ResiliencePolicyBuilder(operationKey);

    /// <summary>Ready-made presets for common scenarios.</summary>
    public static ResiliencePresets Presets => ResiliencePresets.Instance;

    // -----------------------------------------------------------------------
    // Execute (typed)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes <paramref name="operation"/> applying all configured resilience policies.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="operation">The async operation to protect.</param>
    /// <param name="fallback">Optional typed fallback, activated on unhandled exception.</param>
    /// <param name="cancellationToken">Propagated to the operation and timeout logic.</param>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        FallbackOptions<T>? fallback = null,
        CancellationToken cancellationToken = default)
    {
        var context = BuildContext();
        return await _pipeline.ExecuteAsync(operation, fallback, context, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a parameterless async factory applying all configured resilience policies.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        FallbackOptions<T>? fallback = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(_ => operation(), fallback, cancellationToken)
            .ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Execute (void)
    // -----------------------------------------------------------------------

    /// <summary>Executes a void async operation applying all configured resilience policies.</summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        var context = BuildContext();
        await _pipeline.ExecuteAsync(operation, context, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Executes a parameterless void async operation.</summary>
    public async Task ExecuteAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(_ => operation(), cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private ResilienceContext BuildContext()
    {
        var ctx = new ResilienceContext();
        ctx.OperationKey = _operationKey;
        ctx.Reset();
        return ctx;
    }
}

/// <summary>
/// A composed resilience policy that includes a typed <see cref="FallbackOptions{T}"/>.
/// Returned by <see cref="ResiliencePolicyBuilder.Fallback{T}"/>.
/// </summary>
/// <typeparam name="T">The expected return type.</typeparam>
public sealed class ResiliencePolicy<T>
{
    private readonly ResiliencePipeline _pipeline;
    private readonly FallbackOptions<T> _fallback;
    internal readonly string? _operationKey;

    internal ResiliencePolicy(
        string? operationKey,
        RetryOptions? retry,
        CircuitBreakerOptions? circuitBreaker,
        TimeoutOptions? timeout,
        BulkheadOptions? bulkhead,
        HedgeOptions? hedge,
        RateLimiterOptions? rateLimiter,
        ChaosOptions? chaos,
        FallbackOptions<T> fallback,
        ICircuitBreakerRegistry registry)
    {
        _operationKey = operationKey;
        _fallback = fallback;
        _pipeline = new ResiliencePipeline(retry, circuitBreaker, timeout, bulkhead, hedge, rateLimiter, chaos, registry);
    }

    /// <summary>Executes <paramref name="operation"/> with the embedded fallback and all other configured policies.</summary>
    public async Task<T> ExecuteAsync(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var ctx = new ResilienceContext();
        ctx.OperationKey = _operationKey;
        ctx.Reset();
        return await _pipeline.ExecuteAsync(operation, _fallback, ctx, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Parameterless overload.</summary>
    public Task<T> ExecuteAsync(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        => ExecuteAsync(_ => operation(), cancellationToken);
}
