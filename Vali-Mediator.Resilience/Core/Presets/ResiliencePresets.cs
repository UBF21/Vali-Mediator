using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Core.Presets;

/// <summary>
/// Ready-made resilience policies for the most common scenarios.
/// Access via <see cref="ResiliencePolicy.Presets"/>.
/// </summary>
public sealed class ResiliencePresets
{
    internal static readonly ResiliencePresets Instance = new ResiliencePresets();

    private ResiliencePresets() { }

    // -----------------------------------------------------------------------
    // Presets
    // -----------------------------------------------------------------------

    /// <summary>
    /// Balanced policy for external REST/HTTP API calls.
    /// <list type="bullet">
    ///   <item>Retry: 3 attempts, exponential jitter (200 ms → 5 s cap)</item>
    ///   <item>Circuit Breaker: opens after 5 failures in 30 s, recovers after 30 s</item>
    ///   <item>Timeout: 15 s optimistic</item>
    /// </list>
    /// </summary>
    public ResiliencePolicy ForExternalApi(string? operationKey = "external-api")
        => ResiliencePolicy.Create(operationKey)
            .Retry(o =>
            {
                o.MaxRetries = 3;
                o.BackoffType = BackoffType.ExponentialWithJitter;
                o.InitialDelay = TimeSpan.FromMilliseconds(200);
                o.MaxDelay = TimeSpan.FromSeconds(5);
            })
            .CircuitBreaker(o =>
            {
                o.CircuitKey = operationKey ?? "external-api";
                o.FailureThreshold = 5;
                o.SamplingDuration = TimeSpan.FromSeconds(30);
                o.BreakDuration = TimeSpan.FromSeconds(30);
                o.HalfOpenMaxAttempts = 2;
            })
            .Timeout(TimeSpan.FromSeconds(15))
            .Build();

    /// <summary>
    /// Conservative policy for database operations.
    /// <list type="bullet">
    ///   <item>Retry: 2 attempts, linear backoff (500 ms, 1 s)</item>
    ///   <item>Circuit Breaker: opens after 3 failures in 20 s, recovers after 15 s</item>
    ///   <item>Timeout: 30 s optimistic</item>
    ///   <item>Bulkhead: max 20 concurrent queries</item>
    /// </list>
    /// </summary>
    public ResiliencePolicy ForDatabase(string? operationKey = "database")
        => ResiliencePolicy.Create(operationKey)
            .Retry(o =>
            {
                o.MaxRetries = 2;
                o.BackoffType = BackoffType.Linear;
                o.InitialDelay = TimeSpan.FromMilliseconds(500);
                o.MaxDelay = TimeSpan.FromSeconds(3);
            })
            .CircuitBreaker(o =>
            {
                o.CircuitKey = operationKey ?? "database";
                o.FailureThreshold = 3;
                o.SamplingDuration = TimeSpan.FromSeconds(20);
                o.BreakDuration = TimeSpan.FromSeconds(15);
            })
            .Timeout(TimeSpan.FromSeconds(30))
            .Bulkhead(20, 5)
            .Build();

    /// <summary>
    /// Aggressive policy for critical operations (payments, auth tokens) that must not fail silently.
    /// <list type="bullet">
    ///   <item>Retry: 5 attempts, exponential jitter (100 ms → 10 s cap)</item>
    ///   <item>Circuit Breaker: rate-based — opens if 50% of 20+ calls fail in 60 s; recovers after 60 s</item>
    ///   <item>Timeout: 20 s optimistic</item>
    ///   <item>Bulkhead: max 5 concurrent (prevent overload)</item>
    /// </list>
    /// </summary>
    public ResiliencePolicy ForCritical(string? operationKey = "critical")
        => ResiliencePolicy.Create(operationKey)
            .Retry(o =>
            {
                o.MaxRetries = 5;
                o.BackoffType = BackoffType.ExponentialWithJitter;
                o.InitialDelay = TimeSpan.FromMilliseconds(100);
                o.MaxDelay = TimeSpan.FromSeconds(10);
            })
            .CircuitBreaker(o =>
            {
                o.CircuitKey = operationKey ?? "critical";
                o.FailureRateThreshold = 0.5;
                o.MinimumThroughput = 20;
                o.SamplingDuration = TimeSpan.FromSeconds(60);
                o.BreakDuration = TimeSpan.FromSeconds(60);
                o.HalfOpenMaxAttempts = 1;
            })
            .Timeout(TimeSpan.FromSeconds(20))
            .Bulkhead(5, 10)
            .Build();

    /// <summary>
    /// Pass-through policy — no resilience applied.
    /// Useful as a default/null-object in generic contexts.
    /// </summary>
    public ResiliencePolicy NoResilience()
        => ResiliencePolicy.Create("no-resilience").Build();
}
