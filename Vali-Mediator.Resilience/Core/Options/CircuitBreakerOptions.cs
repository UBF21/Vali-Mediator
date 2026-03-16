using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Enums;

namespace Vali_Mediator_Resilience.Core.Options;

/// <summary>
/// Configuration for circuit-breaker behaviour.
/// </summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>
    /// Unique key that identifies this circuit across the application.
    /// Circuits with the same key share state via the <see cref="Vali_Mediator_Resilience.Core.Registry.ICircuitBreakerRegistry"/>.
    /// Required — must be set before building the policy.
    /// </summary>
    public string CircuitKey { get; set; } = string.Empty;

    /// <summary>
    /// Absolute number of failures within <see cref="SamplingDuration"/> that will open the circuit.
    /// Evaluated when <see cref="FailureRateThreshold"/> is 0 (default).
    /// Default: 5.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Failure rate (0.0–1.0) that will open the circuit, once
    /// <see cref="MinimumThroughput"/> calls have been observed in <see cref="SamplingDuration"/>.
    /// When > 0, takes precedence over <see cref="FailureThreshold"/>.
    /// Default: 0 (disabled).
    /// </summary>
    public double FailureRateThreshold { get; set; } = 0;

    /// <summary>
    /// Minimum number of calls within <see cref="SamplingDuration"/> before failure-rate
    /// evaluation begins. Prevents opening on very sparse traffic.
    /// Default: 10.
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Rolling time window used to count failures / throughput.
    /// Default: 60 s.
    /// </summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the circuit stays <see cref="CircuitState.Open"/> before transitioning to
    /// <see cref="CircuitState.HalfOpen"/>. Default: 30 s.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of probe requests allowed in <see cref="CircuitState.HalfOpen"/>.
    /// If all probes succeed the circuit closes; if any fails it reopens.
    /// Default: 1.
    /// </summary>
    public int HalfOpenMaxAttempts { get; set; } = 1;

    /// <summary>Called when the circuit transitions to <see cref="CircuitState.Open"/>.</summary>
    public Func<ResilienceContext, Exception?, Task>? OnOpen { get; set; }

    /// <summary>Called when the circuit transitions back to <see cref="CircuitState.Closed"/>.</summary>
    public Func<ResilienceContext, Task>? OnClose { get; set; }

    /// <summary>Called when the circuit transitions to <see cref="CircuitState.HalfOpen"/>.</summary>
    public Func<ResilienceContext, Task>? OnHalfOpen { get; set; }
}
