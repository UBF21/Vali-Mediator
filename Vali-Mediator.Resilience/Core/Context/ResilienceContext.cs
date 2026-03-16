namespace Vali_Mediator_Resilience.Core.Context;

/// <summary>
/// Carries contextual information through the resilience pipeline and into callbacks.
/// A fresh instance is created at the start of each top-level <c>ExecuteAsync</c> call.
/// </summary>
public sealed class ResilienceContext
{
    /// <summary>Zero-based index of the current attempt (0 = first attempt, 1 = first retry, …).</summary>
    public int AttemptNumber { get; internal set; }

    /// <summary>Time elapsed since the operation started (updated before every callback).</summary>
    public TimeSpan ElapsedTime { get; internal set; }

    /// <summary>The exception thrown by the last attempt, if any.</summary>
    public Exception? LastException { get; internal set; }

    /// <summary>
    /// Optional key identifying the logical operation (e.g. "payment-gateway").
    /// Populated from <see cref="Vali_Mediator_Resilience.Core.Policies.ResiliencePolicyBuilder.OperationKey"/>.
    /// </summary>
    public string? OperationKey { get; internal set; }

    /// <summary>The token from the outermost <c>ExecuteAsync</c> call.</summary>
    public CancellationToken CancellationToken { get; internal set; }

    /// <summary>
    /// Arbitrary user-defined properties that callbacks can read and write.
    /// Keyed by string; typed access helpers are provided via extension methods.
    /// </summary>
    public Dictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

    internal ResilienceContext() { }

    internal void Reset()
    {
        AttemptNumber = 0;
        ElapsedTime = TimeSpan.Zero;
        LastException = null;
    }
}
