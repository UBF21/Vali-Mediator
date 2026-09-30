namespace Vali_Mediator_Resilience.Core.Options;

/// <summary>
/// Process-wide limits for resilience components. Register with <c>AddResilienceOptions</c>.
/// </summary>
public sealed class ResilienceGlobalOptions
{
    private int _maxSharedStates = 10_000;

    /// <summary>
    /// Maximum number of distinct <c>WithSharedState</c> keys (each key holds bulkhead, rate limiter and
    /// circuit-breaker state). Exceeding it throws <see cref="InvalidOperationException"/> so that unbounded,
    /// client-controlled keys cannot exhaust memory. Default: 10,000.
    /// </summary>
    public int MaxSharedStates
    {
        get => _maxSharedStates;
        set => _maxSharedStates = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxSharedStates), value, "Must be greater than zero.");
    }
}
