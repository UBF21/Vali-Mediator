using System.Collections.Concurrent;
using System.Diagnostics;
using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>
/// Enforces a Token Bucket or Sliding Window rate limit without external dependencies.
/// One instance per <see cref="Policies.ResiliencePolicy"/> — share instances (via singleton DI) when
/// you want a global limit across multiple call sites.
/// </summary>
public sealed class RateLimiterState : IDisposable
{
    private static readonly TimeSpan MinWait = TimeSpan.FromMilliseconds(1);

    private readonly RateLimiterOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    // ---- Token Bucket state ----
    private int _tokens;
    private DateTimeOffset _lastReplenishment;

    // ---- Sliding Window state ----
    private readonly ConcurrentQueue<DateTimeOffset> _callTimestamps = new ConcurrentQueue<DateTimeOffset>();

    // ---- Shared lock ----
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private bool _disposed;

    /// <summary>Creates a limiter driven by <paramref name="options"/> (validated on construction).</summary>
    public RateLimiterState(RateLimiterOptions options) : this(options, () => MonotonicClock.Now)
    {
    }

    internal RateLimiterState(RateLimiterOptions options, Func<DateTimeOffset> clock)
    {
        OptionsValidator.Validate(options);
        _options = options;
        _clock = clock;
        _tokens = options.BucketCapacity;
        _lastReplenishment = clock();
    }

    /// <summary>
    /// Returns <c>true</c> if the call is permitted; <c>false</c> if it should be rejected.
    /// When <see cref="RateLimiterOptions.QueueTimeout"/> > zero and there is temporary
    /// capacity, the caller waits up to that duration.
    /// </summary>
    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var timeout = _options.QueueTimeout;
        long startTimestamp = Stopwatch.GetTimestamp();

        while (true)
        {
            TimeSpan untilNextPermit;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                bool permitted = _options.Algorithm == RateLimiterAlgorithm.SlidingWindow
                    ? TryAcquireSlidingWindow()
                    : TryAcquireTokenBucket();

                if (permitted) return true;
                untilNextPermit = TimeUntilNextPermit();
            }
            finally
            {
                _gate.Release();
            }

            if (timeout <= TimeSpan.Zero) return false;

            var remaining = timeout - Stopwatch.GetElapsedTime(startTimestamp);
            if (remaining <= TimeSpan.Zero) return false;

            // Sleep exactly until a permit can exist (bounded by the deadline) instead of polling.
            var pause = untilNextPermit < remaining ? untilNextPermit : remaining;
            await Task.Delay(pause < MinWait ? MinWait : pause, cancellationToken).ConfigureAwait(false);
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
        var now = _clock();
        if (_options.TokensPerInterval == 0) return; // replenishment disabled: the bucket only drains

        var interval = _options.ReplenishmentInterval;
        var elapsed = now - _lastReplenishment;

        if (elapsed < TimeSpan.Zero)
        {
            _lastReplenishment = now; // clock moved backwards: rebase instead of stalling the refill
            return;
        }

        long intervals = elapsed.Ticks / interval.Ticks;
        if (intervals <= 0) return;

        long missing = _options.BucketCapacity - _tokens;
        long intervalsToFill = (missing + _options.TokensPerInterval - 1) / _options.TokensPerInterval;

        if (intervals >= intervalsToFill)
        {
            _tokens = _options.BucketCapacity;
            _lastReplenishment = now; // full bucket: nothing to carry over
        }
        else
        {
            // intervals < intervalsToFill keeps the product below the bucket capacity, so it cannot overflow.
            _tokens += (int)(intervals * _options.TokensPerInterval);
            _lastReplenishment += TimeSpan.FromTicks(interval.Ticks * intervals); // keep the fractional remainder
        }
    }

    private bool TryAcquireSlidingWindow()
    {
        var now = _clock();
        Evict(now);

        if (_callTimestamps.Count >= _options.PermitLimit)
            return false;

        _callTimestamps.Enqueue(now);
        return true;
    }

    private void Evict(DateTimeOffset now)
    {
        var windowStart = now - _options.Window;
        while (_callTimestamps.TryPeek(out var oldest) && oldest < windowStart)
            _callTimestamps.TryDequeue(out _);
    }

    private TimeSpan TimeUntilNextPermit()
    {
        var now = _clock();
        TimeSpan wait;

        if (_options.Algorithm == RateLimiterAlgorithm.SlidingWindow)
            wait = _callTimestamps.TryPeek(out var oldest) ? oldest + _options.Window - now : MinWait;
        else
            wait = _options.ReplenishmentInterval - (now - _lastReplenishment);

        return wait < MinWait ? MinWait : wait;
    }

    /// <summary>Releases the internal lock.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}

/// <summary>Adapts <see cref="RateLimiterExecutor"/> to the middleware chain, owning (or sharing) its state.</summary>
internal sealed class RateLimiterMiddleware : IResilienceMiddleware
{
    private readonly RateLimiterOptions _options;
    private readonly RateLimiterState? _globalState;
    private readonly PartitionedRateLimiterState? _partitionedState;

    internal RateLimiterMiddleware(RateLimiterOptions options, string? stateKey)
    {
        OptionsValidator.Validate(options);
        _options = options;

        if (options.PartitionKeyResolver != null)
        {
            _partitionedState = stateKey == null
                ? new PartitionedRateLimiterState(options)
                : SharedPolicyStates.Store.GetOrAdd(stateKey, "ratelimiter-partitioned", () => new PartitionedRateLimiterState(options));
        }
        else
        {
            _globalState = stateKey == null
                ? new RateLimiterState(options)
                : SharedPolicyStates.Store.GetOrAdd(stateKey, "ratelimiter", () => new RateLimiterState(options));
        }
    }

    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> next,
        ResilienceContext context,
        CancellationToken cancellationToken)
        => RateLimiterExecutor.ExecuteAsync(next, _globalState, _partitionedState, _options, context, cancellationToken);
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
            if (!context.Properties.TryGetValue(ResilienceContext.RequestKey, out var req) || req == null)
                throw new InvalidOperationException(
                    "RateLimiterOptions.PartitionKeyResolver is set but no request was found in ResilienceContext. " +
                    "Dispatch the request through ResilienceBehavior, or call ResiliencePolicy.ExecuteForRequestAsync(request, ...) when using the policy directly.");

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
