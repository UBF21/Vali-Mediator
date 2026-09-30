namespace Vali_Mediator_Caching.Pipeline;

/// <summary>
/// Configuration options for <see cref="CachingBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <remarks>
/// Register with <c>services.AddCachingOptions(o => ...)</c>. Without registration the defaults apply.
/// </remarks>
public sealed class CachingOptions
{
    private TimeSpan _coalescingWaitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a caller waits for another in-flight execution of the same cache key
    /// before running the handler itself. This bounds the damage of a handler that hangs.
    /// Use <see cref="Timeout.InfiniteTimeSpan"/> to wait without a limit. Defaults to <c>30 seconds</c>.
    /// </summary>
    public TimeSpan CoalescingWaitTimeout
    {
        get => _coalescingWaitTimeout;
        set
        {
            if (value <= TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(
                    nameof(CoalescingWaitTimeout), value, "Must be greater than zero or Timeout.InfiniteTimeSpan.");
            _coalescingWaitTimeout = value;
        }
    }
}
