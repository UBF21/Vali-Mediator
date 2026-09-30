using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>Concurrency slots plus the count of callers waiting for one; shareable between policies.</summary>
internal sealed class BulkheadState
{
    internal BulkheadState(int maxConcurrentCalls)
    {
        Semaphore = new SemaphoreSlim(maxConcurrentCalls, maxConcurrentCalls);
    }

    internal SemaphoreSlim Semaphore { get; }
    internal int Queued;
}

/// <summary>Limits concurrent calls; excess calls wait in a bounded queue or are rejected.</summary>
internal sealed class BulkheadMiddleware : IResilienceMiddleware
{
    private readonly BulkheadOptions _options;
    private readonly BulkheadState _state;

    internal BulkheadMiddleware(BulkheadOptions options, string? stateKey)
    {
        _options = options;
        _state = stateKey == null
            ? new BulkheadState(options.MaxConcurrentCalls)
            : SharedPolicyStates.Store.GetOrAdd(stateKey, "bulkhead", () => new BulkheadState(options.MaxConcurrentCalls));
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> next,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        bool acquired = await _state.Semaphore.WaitAsync(0).ConfigureAwait(false);
        if (!acquired && _options.MaxQueuedCalls > 0)
            acquired = await WaitInQueueAsync(cancellationToken).ConfigureAwait(false);

        if (!acquired)
        {
            if (_options.OnRejected != null)
                await _options.OnRejected(context).ConfigureAwait(false);

            throw new BulkheadRejectedException(_options.MaxConcurrentCalls, _options.MaxQueuedCalls);
        }

        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _state.Semaphore.Release();
        }
    }

    private async Task<bool> WaitInQueueAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _state.Queued) > _options.MaxQueuedCalls)
        {
            Interlocked.Decrement(ref _state.Queued);
            return false;
        }

        try
        {
            // InfiniteTimeSpan (the default) waits until a slot frees up.
            return await _state.Semaphore.WaitAsync(_options.QueueTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _state.Queued);
        }
    }
}
