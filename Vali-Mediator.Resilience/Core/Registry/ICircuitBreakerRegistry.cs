using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Registry;

/// <summary>
/// Manages named circuit breaker states across the application lifetime.
/// Register as a singleton in DI so all components sharing a key share state.
/// </summary>
public interface ICircuitBreakerRegistry
{
    /// <summary>
    /// Returns (and lazily creates) the <see cref="CircuitBreakerState"/> for <paramref name="circuitKey"/>.
    /// If the circuit does not exist yet, it is created from <paramref name="options"/>.
    /// </summary>
    CircuitBreakerState GetOrCreate(string circuitKey, CircuitBreakerOptions options);

    /// <summary>
    /// Returns the current <see cref="CircuitState"/> of a named circuit,
    /// or <c>null</c> if no circuit with that key has been registered yet.
    /// </summary>
    CircuitState? GetState(string circuitKey);

    /// <summary>Manually resets a named circuit to <see cref="CircuitState.Closed"/>.</summary>
    void Reset(string circuitKey);

    /// <summary>Removes all circuit states from the registry.</summary>
    void Clear();
}
