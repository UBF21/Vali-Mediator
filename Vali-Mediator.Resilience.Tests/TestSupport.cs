using System.Collections.Concurrent;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Registry;

namespace Vali_Mediator_Resilience.Tests;

/// <summary>Hand-driven clock so time-dependent behavior is tested without sleeping.</summary>
internal sealed class ManualClock
{
    private DateTimeOffset _now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal DateTimeOffset Read() => _now;

    internal void Advance(TimeSpan by) => _now += by;
}

/// <summary>Circuit-breaker registry whose breakers read time from a <see cref="ManualClock"/>.</summary>
internal sealed class ClockedRegistry : ICircuitBreakerRegistry
{
    private readonly ManualClock _clock;
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _states = new ConcurrentDictionary<string, CircuitBreakerState>();

    internal ClockedRegistry(ManualClock clock)
    {
        _clock = clock;
    }

    public CircuitBreakerState GetOrCreate(string circuitKey, CircuitBreakerOptions options)
        => _states.GetOrAdd(circuitKey, _ => new CircuitBreakerState(options, _clock.Read));

    public CircuitState? GetState(string circuitKey)
        => _states.TryGetValue(circuitKey, out var state) ? state.State : null;

    public void Reset(string circuitKey)
    {
        if (_states.TryGetValue(circuitKey, out var state)) state.Reset();
    }

    public void Clear() => _states.Clear();
}

internal static class Wait
{
    /// <summary>Polls <paramref name="condition"/> until true; fails after <paramref name="timeout"/> (default 10 s).</summary>
    internal static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var limit = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(5);
        }
    }
}
