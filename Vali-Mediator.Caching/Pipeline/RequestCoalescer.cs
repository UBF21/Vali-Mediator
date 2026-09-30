using System.Collections.Concurrent;

namespace Vali_Mediator_Caching.Pipeline;

/// <summary>
/// Runs one execution per key at a time and shares its outcome with concurrent callers.
/// </summary>
/// <remarks>
/// The first caller for a key (the leader) runs the work; the others wait for its outcome,
/// whether it is cached or not, so a failed <c>Result</c> costs one execution, not one per caller.
/// A waiter never blocks longer than the wait timeout: if the leader is still running by then,
/// the waiter runs the work itself. If the leader is cancelled the waiters retry, and if it throws
/// they observe the same exception.
/// </remarks>
internal sealed class RequestCoalescer<T>
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<T>> _inFlight
        = new ConcurrentDictionary<string, TaskCompletionSource<T>>(StringComparer.Ordinal);

    private int _waiting;

    public int InFlightCount => _inFlight.Count;

    public int WaitingCount => Volatile.Read(ref _waiting);

    public async Task<T> RunAsync(
        string key,
        Func<CancellationToken, Task<T>> work,
        TimeSpan waitTimeout,
        CancellationToken ct)
    {
        while (true)
        {
            var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_inFlight.TryAdd(key, source))
                return await LeadAsync(key, source, work, ct).ConfigureAwait(false);

            if (!_inFlight.TryGetValue(key, out var leader))
                continue; // the leader finished between TryAdd and TryGetValue

            Interlocked.Increment(ref _waiting);
            try
            {
                return await leader.Task.WaitAsync(waitTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException) when (!leader.Task.IsCompleted)
            {
                return await work(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The leader was cancelled, not us: try again, possibly as the new leader.
            }
            finally
            {
                Interlocked.Decrement(ref _waiting);
            }
        }
    }

    private async Task<T> LeadAsync(
        string key,
        TaskCompletionSource<T> source,
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct)
    {
        try
        {
            var result = await work(ct).ConfigureAwait(false);
            source.TrySetResult(result);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            source.TrySetCanceled();
            throw;
        }
        catch (Exception ex)
        {
            source.TrySetException(ex);
            _ = source.Task.Exception; // mark observed even when nobody was waiting
            throw;
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<string, TaskCompletionSource<T>>(key, source));
        }
    }
}
