namespace Vali_Mediator_Resilience.Core.Exceptions;

/// <summary>
/// Thrown when a request is rejected because the circuit breaker is in the
/// <see cref="Vali_Mediator_Resilience.Core.Enums.CircuitState.Open"/> state.
/// </summary>
public sealed class CircuitOpenException : Exception
{
    /// <summary>The circuit breaker key that is currently open.</summary>
    public string CircuitKey { get; }

    /// <summary>Approximate time remaining before the circuit transitions to HalfOpen.</summary>
    public TimeSpan? RetryAfter { get; }

    public CircuitOpenException(string circuitKey, TimeSpan? retryAfter = null)
        : base($"Circuit breaker '{circuitKey}' is open. Requests are blocked until the circuit recovers.")
    {
        CircuitKey = circuitKey;
        RetryAfter = retryAfter;
    }

    public CircuitOpenException(string circuitKey, string message) : base(message)
    {
        CircuitKey = circuitKey;
    }
}
