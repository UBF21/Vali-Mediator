using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Enums;

namespace Vali_Mediator_Resilience.Core.Options;

/// <summary>
/// Configuration for timeout behaviour.
/// </summary>
public sealed class TimeoutOptions
{
    /// <summary>
    /// Maximum duration allowed for the operation.
    /// A <see cref="TimeoutException"/> is thrown if the operation exceeds this limit.
    /// Default: 30 s.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Determines how the timeout is enforced.
    /// Default: <see cref="TimeoutStrategy.Optimistic"/> (CancellationToken-based).
    /// </summary>
    public TimeoutStrategy Strategy { get; set; } = TimeoutStrategy.Optimistic;

    /// <summary>
    /// Called when a timeout fires, before the <see cref="TimeoutException"/> is thrown.
    /// <c>context.ElapsedTime</c> equals the configured <see cref="Timeout"/> at this point.
    /// </summary>
    public Func<ResilienceContext, Task>? OnTimeout { get; set; }
}
