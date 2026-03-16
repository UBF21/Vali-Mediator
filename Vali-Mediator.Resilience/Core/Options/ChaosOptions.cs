namespace Vali_Mediator_Resilience.Core.Options;

/// <summary>
/// Configuration for chaos engineering policy.
/// Randomly injects faults (exceptions, latency, or bad results) into the execution path
/// to verify system resilience. Use only in test/staging environments.
/// </summary>
public sealed class ChaosOptions
{
    /// <summary>
    /// Probability (0.0–1.0) that a chaos fault is injected on any given call.
    /// 0 = disabled, 1 = always inject. Default: 0.
    /// </summary>
    public double InjectionRate { get; set; } = 0;

    /// <summary>When set, this exception is thrown when chaos fires.</summary>
    public Func<Exception>? ExceptionFactory { get; set; }

    /// <summary>
    /// When set (and <see cref="ExceptionFactory"/> is null), injects an artificial delay.
    /// </summary>
    public TimeSpan? LatencyInjection { get; set; }

    /// <summary>
    /// When set (and the two above are null), returns this value as the result instead of
    /// calling the inner operation. Use to simulate degraded responses.
    /// The factory receives the <see cref="Type"/> of the expected return value.
    /// </summary>
    public Func<Type, object?>? ResultFactory { get; set; }

    /// <summary>
    /// Optional custom random source. When null, <see cref="Random.Shared"/> is used.
    /// Useful for deterministic tests.
    /// </summary>
    public Random? Random { get; set; }

    /// <summary>Called before a chaos fault is injected.</summary>
    public Func<Task>? OnChaosInjected { get; set; }

    /// <summary>Returns true if chaos should be injected for this call.</summary>
    internal bool ShouldInject()
    {
        var rng = Random ?? System.Random.Shared;
        return rng.NextDouble() < InjectionRate;
    }
}
