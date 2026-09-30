using Vali_Mediator.Core.Result;
using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Registry;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>Short-circuits calls while the breaker for <see cref="CircuitBreakerOptions.CircuitKey"/> is open.</summary>
internal sealed class CircuitBreakerMiddleware : IResilienceMiddleware
{
    private readonly CircuitBreakerOptions _options;
    private readonly ICircuitBreakerRegistry _registry;

    internal CircuitBreakerMiddleware(CircuitBreakerOptions options, ICircuitBreakerRegistry registry)
    {
        _options = options;
        _registry = registry;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> next,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        var state = _registry.GetOrCreate(_options.CircuitKey, _options);

        if (!state.TryEnter(out int epoch, out bool enteredHalfOpen))
            throw new CircuitOpenException(_options.CircuitKey, state.RetryAfter());

        if (enteredHalfOpen && _options.OnHalfOpen != null)
        {
            try
            {
                await _options.OnHalfOpen(context).ConfigureAwait(false);
            }
            catch
            {
                state.ReleaseProbe(epoch);
                throw;
            }
        }

        T result;
        try
        {
            result = await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // No verdict on the dependency (caller gave up, or another breaker/bulkhead rejected the call):
            // hand the HalfOpen probe back instead of recording a failure.
            if (ex is CircuitOpenException || ex is BulkheadRejectedException
                || IResilienceMiddleware.IsCallerCancellation(ex, context.CancellationToken))
            {
                state.ReleaseProbe(epoch);
                throw;
            }

            if (state.RecordFailureAndCheckOpened(epoch) && _options.OnOpen != null)
                await _options.OnOpen(context, ex).ConfigureAwait(false);

            throw;
        }

        if (result is IResult ir && ir.IsFailure)
        {
            if (state.RecordFailureAndCheckOpened(epoch) && _options.OnOpen != null)
                await _options.OnOpen(context, null).ConfigureAwait(false);
        }
        else if (state.RecordSuccessAndCheckClosed(epoch) && _options.OnClose != null)
        {
            await _options.OnClose(context).ConfigureAwait(false);
        }

        return result;
    }
}
