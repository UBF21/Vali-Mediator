using System.Collections.Concurrent;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>
/// One <see cref="RateLimiterState"/> per partition key, bounded by <see cref="RateLimiterOptions.MaxPartitions"/>.
/// Keys beyond the bound (typically attacker-chosen ones) all share a single overflow limiter, so a flood of
/// unique keys costs constant memory instead of one bucket each.
/// </summary>
internal sealed class PartitionedRateLimiterState : IDisposable
{
    private sealed class Entry
    {
        internal Entry(RateLimiterState state, long now) { State = state; LastAccessTicks = now; }
        internal RateLimiterState State { get; }
        internal long LastAccessTicks;
    }

    private readonly RateLimiterOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _idleTimeout;
    private readonly ConcurrentDictionary<string, Entry> _states = new ConcurrentDictionary<string, Entry>();
    private readonly Lazy<RateLimiterState> _overflow;
    private long _lastSweepTicks;
    private int _count;
    private bool _disposed;

    internal PartitionedRateLimiterState(RateLimiterOptions options) : this(options, () => MonotonicClock.Now)
    {
    }

    internal PartitionedRateLimiterState(RateLimiterOptions options, Func<DateTimeOffset> clock)
    {
        OptionsValidator.Validate(options);
        _options = options;
        _clock = clock;
        _idleTimeout = Max(options.PartitionIdleTimeout, RecoveryTime(options));
        _lastSweepTicks = clock().UtcTicks;
        _overflow = new Lazy<RateLimiterState>(() => new RateLimiterState(options, clock));
    }

    internal int Count => Volatile.Read(ref _count);

    internal RateLimiterState GetOrCreate(string partitionKey)
    {
        long now = _clock().UtcTicks;
        SweepIfDue(now, force: false);

        if (_states.TryGetValue(partitionKey, out var existing))
        {
            Interlocked.Exchange(ref existing.LastAccessTicks, now);
            return existing.State;
        }

        if (Count >= _options.MaxPartitions)
        {
            SweepIfDue(now, force: true);
            if (Count >= _options.MaxPartitions) return _overflow.Value;
        }

        if (Interlocked.Increment(ref _count) > _options.MaxPartitions)
        {
            Interlocked.Decrement(ref _count);
            return _overflow.Value;
        }

        var created = new Entry(new RateLimiterState(_options, _clock), now);
        var winner = _states.GetOrAdd(partitionKey, created);
        if (!ReferenceEquals(winner, created))
        {
            Interlocked.Decrement(ref _count); // another thread created this partition first
            created.State.Dispose();
        }

        Interlocked.Exchange(ref winner.LastAccessTicks, now);
        return winner.State;
    }

    // ponytail: evicted states are not disposed (a caller may still hold one); an in-flight caller on an
    // evicted partition can briefly get a fresh bucket. Fine because eviction only happens after full recovery.
    private void SweepIfDue(long now, bool force)
    {
        long last = Interlocked.Read(ref _lastSweepTicks);
        if (!force && now - last < _idleTimeout.Ticks) return;
        if (Interlocked.CompareExchange(ref _lastSweepTicks, now, last) != last && !force) return;

        foreach (var pair in _states)
        {
            if (now - Interlocked.Read(ref pair.Value.LastAccessTicks) >= _idleTimeout.Ticks
                && ((ICollection<KeyValuePair<string, Entry>>)_states).Remove(pair))
            {
                Interlocked.Decrement(ref _count);
            }
        }
    }

    private static TimeSpan RecoveryTime(RateLimiterOptions o)
    {
        if (o.Algorithm == Enums.RateLimiterAlgorithm.SlidingWindow) return o.Window;
        if (o.TokensPerInterval == 0) return TimeSpan.MaxValue; // never recovers: evicting would grant permits
        long intervals = (o.BucketCapacity + (long)o.TokensPerInterval - 1) / o.TokensPerInterval;
        return TimeSpan.FromTicks(o.ReplenishmentInterval.Ticks * intervals);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _states.Values)
            entry.State.Dispose();
        _states.Clear();
        if (_overflow.IsValueCreated) _overflow.Value.Dispose();
    }
}
