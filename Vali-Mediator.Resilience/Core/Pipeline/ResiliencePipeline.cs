using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>
/// Composes the configured policies, outermost first:
/// Fallback → Chaos → RateLimiter → Retry → Timeout → Circuit Breaker → Bulkhead → Hedge → delegate.
/// (With <see cref="ChaosOptions.InjectPerAttempt"/>, Chaos moves to the innermost position instead.)
/// Each policy is an <see cref="IResilienceMiddleware"/>; the typed Fallback stays here because it depends on
/// the call's return type.
/// </summary>
internal sealed class ResiliencePipeline
{
    private readonly IResilienceMiddleware[] _middlewares;

    internal ResiliencePipeline(PolicyDefinition definition)
    {
        var chaos = definition.Chaos;
        var list = new List<IResilienceMiddleware>(8);

        if (chaos != null && !chaos.InjectPerAttempt) list.Add(new ChaosMiddleware(chaos));
        if (definition.RateLimiter != null) list.Add(new RateLimiterMiddleware(definition.RateLimiter, definition.StateKey));
        list.Add(new RetryMiddleware(definition.Retry));
        if (definition.Timeout != null) list.Add(new TimeoutMiddleware(definition.Timeout));
        if (definition.CircuitBreaker != null) list.Add(new CircuitBreakerMiddleware(definition.CircuitBreaker, definition.Registry));
        if (definition.Bulkhead != null) list.Add(new BulkheadMiddleware(definition.Bulkhead, definition.StateKey));
        if (definition.Hedge != null) list.Add(new HedgeMiddleware(definition.Hedge));
        if (chaos != null && chaos.InjectPerAttempt) list.Add(new ChaosMiddleware(chaos));

        _middlewares = list.ToArray();
    }

    /// <summary>Executes the operation with typed return value applying all resilience policies.</summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        FallbackOptions<T>? fallback,
        ResilienceContext context,
        CancellationToken cancellationToken = default)
    {
        context.CancellationToken = cancellationToken;
        var chain = Compose(operation, context);

        if (fallback == null)
            return await chain(cancellationToken).ConfigureAwait(false);

        T value;
        try
        {
            value = await chain(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IResilienceMiddleware.IsCallerCancellation(ex, cancellationToken)
                                   && ShouldActivateFallback(fallback, ex))
        {
            return await ActivateFallbackAsync(fallback, context, ex).ConfigureAwait(false);
        }

        // A failed-but-non-throwing result (e.g. Result.Fail) can also be replaced.
        if (fallback.FallbackOnResultPredicate != null && fallback.FallbackOnResultPredicate(value))
            return await ActivateFallbackAsync(fallback, context, null).ConfigureAwait(false);

        return value;
    }

    /// <summary>Executes a void operation applying all resilience policies.</summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        ResilienceContext context,
        CancellationToken cancellationToken = default)
    {
        context.CancellationToken = cancellationToken;

        Func<CancellationToken, Task<object?>> wrapped = async ct =>
        {
            await operation(ct).ConfigureAwait(false);
            return null;
        };

        await Compose(wrapped, context)(cancellationToken).ConfigureAwait(false);
    }

    private Func<CancellationToken, Task<T>> Compose<T>(
        Func<CancellationToken, Task<T>> operation,
        ResilienceContext context)
    {
        var chain = operation;
        for (int i = _middlewares.Length - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = chain;
            chain = ct => middleware.ExecuteAsync(next, context, ct);
        }

        return chain;
    }

    private static async Task<T> ActivateFallbackAsync<T>(FallbackOptions<T> fallback, ResilienceContext context, Exception? ex)
    {
        if (fallback.OnFallback != null)
            await fallback.OnFallback(context, ex).ConfigureAwait(false);

        return await ResolveFallbackValue(fallback, context).ConfigureAwait(false);
    }

    private static bool ShouldActivateFallback<T>(FallbackOptions<T> fallback, Exception ex)
        => fallback.FallbackOnException == null || fallback.FallbackOnException(ex);

    private static async Task<T> ResolveFallbackValue<T>(FallbackOptions<T> fallback, ResilienceContext context)
    {
        if (fallback.FallbackFactory != null)
            return await fallback.FallbackFactory(context).ConfigureAwait(false);

        return fallback.FallbackValue!;
    }
}
