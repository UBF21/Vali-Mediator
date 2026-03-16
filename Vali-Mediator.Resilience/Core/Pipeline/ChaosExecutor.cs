using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>
/// Injects random faults (exceptions, latency, or synthetic results) to simulate failures.
/// </summary>
internal static class ChaosExecutor
{
    internal static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ChaosOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.ShouldInject())
            return await operation(cancellationToken).ConfigureAwait(false);

        if (options.OnChaosInjected != null)
            await options.OnChaosInjected().ConfigureAwait(false);

        // Priority: exception > latency > result
        if (options.ExceptionFactory != null)
            throw options.ExceptionFactory();

        if (options.LatencyInjection.HasValue)
        {
            await Task.Delay(options.LatencyInjection.Value, cancellationToken).ConfigureAwait(false);
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        if (options.ResultFactory != null)
        {
            var synthetic = options.ResultFactory(typeof(T));
            return synthetic is T typed ? typed : default!;
        }

        return await operation(cancellationToken).ConfigureAwait(false);
    }
}
