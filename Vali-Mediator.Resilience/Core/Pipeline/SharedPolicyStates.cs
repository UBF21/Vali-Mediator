using System.Collections.Concurrent;
using Vali_Mediator_Resilience.Core.Registry;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>Capped, keyed store of stateful policy components shared between policies.</summary>
internal sealed class PolicyStateStore
{
    private volatile int _maxEntries;
    private readonly ConcurrentDictionary<string, object> _states
        = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);

    internal PolicyStateStore(int maxEntries)
    {
        _maxEntries = maxEntries;
    }

    internal int Count => _states.Count;

    internal int Limit => _maxEntries;

    internal void SetLimit(int maxEntries) => _maxEntries = maxEntries;

    /// <summary>The first caller for a key defines the state; later callers with the same key reuse it.</summary>
    internal T GetOrAdd<T>(string stateKey, string kind, Func<T> factory) where T : class
    {
        string fullKey = kind + ":" + stateKey;
        if (_states.TryGetValue(fullKey, out var existing))
            return (T)existing;

        if (_states.Count >= _maxEntries)
            throw new InvalidOperationException(
                $"Shared resilience state is limited to {_maxEntries} entries; use a small set of fixed keys with WithSharedState().");

        return (T)_states.GetOrAdd(fullKey, _ => factory());
    }
}

/// <summary>
/// Process-wide state shared by every <see cref="Policies.ResiliencePolicy"/> built with the same
/// <see cref="Policies.ResiliencePolicyBuilder.WithSharedState"/> key. Keys are developer-defined names,
/// never request data, and the store is capped so a misuse cannot grow it without bound.
/// </summary>
internal static class SharedPolicyStates
{
    internal const int MaxEntries = 10_000;

    internal static readonly PolicyStateStore Store = new PolicyStateStore(MaxEntries);

    internal static readonly ICircuitBreakerRegistry Circuits = new CircuitBreakerRegistry();
}
