using System.Collections.Concurrent;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Registry;

/// <summary>
/// Default in-memory implementation of <see cref="ICircuitBreakerRegistry"/>.
/// Thread-safe; register as singleton.
/// </summary>
public sealed class CircuitBreakerRegistry : ICircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuits
        = new ConcurrentDictionary<string, CircuitBreakerState>(StringComparer.Ordinal);

    /// <inheritdoc/>
    public CircuitBreakerState GetOrCreate(string circuitKey, CircuitBreakerOptions options)
    {
        if (string.IsNullOrWhiteSpace(circuitKey))
            throw new ArgumentException("Circuit key must not be null or whitespace.", nameof(circuitKey));

        return _circuits.GetOrAdd(circuitKey, _ => new CircuitBreakerState(options));
    }

    /// <inheritdoc/>
    public CircuitState? GetState(string circuitKey)
    {
        if (_circuits.TryGetValue(circuitKey, out var state))
            return state.State;
        return null;
    }

    /// <inheritdoc/>
    public void Reset(string circuitKey)
    {
        if (_circuits.TryGetValue(circuitKey, out var state))
            state.Reset();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _circuits.Clear();
    }
}
