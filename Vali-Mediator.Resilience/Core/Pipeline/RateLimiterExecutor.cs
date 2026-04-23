using System.Collections.Concurrent;
using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>
/// Enforces a Token Bucket or Sliding Window rate limit without external dependencies.
/// One instance per <see cref="ResiliencePolicy"/> — share instances (via singleton DI) when
/// you want a global limit across multiple call sites.
/// </summary>
public sealed class RateLimiterState : IDisposable
{
    private readonly RateLimiterOptions _options;

    // ---- Token Bucket state ----
    private int _tokens;
    private DateTimeOffset _lastReplenishment;

    // ---- Sliding Window state ----
    private readonly ConcurrentQueue<DateTimeOffset> _callTimestamps = new ConcurrentQueue<DateTimeOffset>();

    // ---- Shared lock ----
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private bool _disposed;

    public RateLimiterState(RateLimiterOptions options)
    {
        _options = options;
        _tokens = options.BucketCapacity;
        _lastReplenishment = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Returns <c>true</c> if the call is permitted; <c>false</c> if it should be rejected.
    /// When <see cref="RateLimiterOptions.QueueTimeout"/> > zero and there is temporary
    /// capacity, the caller waits up to that duration.
    /// </summary>
    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var timeout = _options.QueueTimeout;
        var deadline = timeout > TimeSpan.Zero
            ? DateTimeOffset.UtcNow + timeout
            : DateTimeOffset.MinValue;

        while (true)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                bool permitted = _options.Algorithm == RateLimiterAlgorithm.SlidingWindow
                    ? TryAcquireSlidingWindow()
                    : TryAcquireTokenBucket();

                if (permitted) return true;
            }
            finally
            {
                _gate.Release();
            }

            if (timeout == TimeSpan.Zero || DateTimeOffset.UtcNow >= deadline)
                return false;

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    // -----------------------------------------------------------------------

    private bool TryAcquireTokenBucket()
    {
        Replenish();
        if (_tokens <= 0) return false;
        _tokens--;
        return true;
    }

    private void Replenish()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastReplenishment;
        int intervals = (int)(elapsed.TotalMilliseconds / _options.ReplenishmentInterval.TotalMilliseconds);
        if (intervals <= 0) return;

        _tokens = Math.Min(_options.BucketCapacity, _tokens + intervals * _options.TokensPerInterval);
        _lastReplenishment = now;
    }

    private bool TryAcquireSlidingWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - _options.Window;

        // Evict old entries
        while (_callTimestamps.TryPeek(out var oldest) && oldest < windowStart)
            _callTimestamps.TryDequeue(out _);

        if (_callTimestamps.Count >= _options.PermitLimit)
            return false;

        _callTimestamps.Enqueue(now);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}

internal static class RateLimiterExecutor
{
    internal static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        RateLimiterState? globalState,
        PartitionedRateLimiterState? partitionedState,
        RateLimiterOptions options,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        RateLimiterState stateToUse;

        if (partitionedState != null)
        {
            if (!context.Properties.TryGetValue("Vali.Request", out var req) || req == null)
                throw new InvalidOperationException(
                    "RateLimiterOptions.PartitionKeyResolver is set but no request was found in ResilienceContext. " +
                    "Ensure the request is dispatched through ResilienceBehavior.");

            stateToUse = partitionedState.GetOrCreate(options.PartitionKeyResolver!(req));
        }
        else
        {
            stateToUse = globalState!;
        }

        bool permitted = await stateToUse.TryAcquireAsync(cancellationToken).ConfigureAwait(false);

        if (!permitted)
        {
            if (options.OnRejected != null)
                await options.OnRejected(context).ConfigureAwait(false);

            throw new Exceptions.RateLimitExceededException(options.Algorithm.ToString());
        }

        return await operation(cancellationToken).ConfigureAwait(false);
    }
}
