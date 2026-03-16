using Vali_Mediator_Resilience.Core.Context;

namespace Vali_Mediator_Resilience.Core.Options;

/// <summary>
/// Configuration for hedge policy.
/// A hedge launches a second (or more) parallel request after a configurable delay.
/// The first successful response wins; all others are cancelled.
/// </summary>
public sealed class HedgeOptions
{
    /// <summary>
    /// How long to wait before launching the first hedge attempt.
    /// If the original call completes within this window, no hedge is fired.
    /// Default: 1 s.
    /// </summary>
    public TimeSpan HedgeDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum number of parallel hedge attempts (excluding the original).
    /// Default: 1 (one original + one hedge = two parallel calls).
    /// </summary>
    public int MaxHedgedAttempts { get; set; } = 1;

    /// <summary>
    /// Predicate evaluated on the result of a call. When it returns <c>true</c>
    /// the result is treated as unsuccessful and another hedge is launched (up to
    /// <see cref="MaxHedgedAttempts"/>). When <c>null</c>, the first call to
    /// complete without throwing is accepted.
    /// </summary>
    public Func<object?, bool>? ShouldHedgeOnResult { get; set; }

    /// <summary>
    /// Predicate evaluated on exceptions thrown by a call attempt. When <c>true</c>
    /// another hedge is launched. When <c>null</c>, exceptions from early attempts are
    /// suppressed and the next attempt is awaited.
    /// </summary>
    public Func<Exception, bool>? ShouldHedgeOnException { get; set; }

    /// <summary>
    /// Called each time a new hedge attempt is launched.
    /// <c>context.AttemptNumber</c> reflects which hedge this is (1 = first hedge, …).
    /// </summary>
    public Func<ResilienceContext, Task>? OnHedge { get; set; }
}
