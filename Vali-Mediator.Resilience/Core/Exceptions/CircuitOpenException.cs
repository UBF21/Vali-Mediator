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

    /// <summary>Creates the exception for the open circuit <paramref name="circuitKey"/>.</summary>
    /// <param name="circuitKey">Key of the open circuit.</param>
    /// <param name="retryAfter">Approximate time before the circuit tries to recover.</param>
    public CircuitOpenException(string circuitKey, TimeSpan? retryAfter = null)
        : base($"Circuit breaker '{circuitKey}' is open. Requests are blocked until the circuit recovers.")
    {
        CircuitKey = circuitKey;
        RetryAfter = retryAfter;
    }

    /// <summary>Creates the exception with a custom message.</summary>
    /// <param name="circuitKey">Key of the open circuit.</param>
    /// <param name="message">The error message.</param>
    public CircuitOpenException(string circuitKey, string message) : base(message)
    {
        CircuitKey = circuitKey;
    }
}
