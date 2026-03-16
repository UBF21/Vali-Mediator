namespace Vali_Mediator_Resilience.Core.Enums;

/// <summary>
/// The state of a circuit breaker.
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Normal operation. Requests pass through. Failures are tracked.
    /// Transitions to <see cref="Open"/> when the failure threshold is exceeded.
    /// </summary>
    Closed = 0,

    /// <summary>
    /// The circuit is tripped. All requests immediately throw
    /// <see cref="Vali_Mediator_Resilience.Core.Exceptions.CircuitOpenException"/>.
    /// Transitions to <see cref="HalfOpen"/> after <c>BreakDuration</c> elapses.
    /// </summary>
    Open = 1,

    /// <summary>
    /// A trial period. A limited number of requests are allowed through to test recovery.
    /// Success → <see cref="Closed"/>. Failure → back to <see cref="Open"/>.
    /// </summary>
    HalfOpen = 2
}
