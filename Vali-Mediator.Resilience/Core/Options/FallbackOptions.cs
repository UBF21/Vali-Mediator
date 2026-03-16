using Vali_Mediator_Resilience.Core.Context;

namespace Vali_Mediator_Resilience.Core.Options;

/// <summary>
/// Configuration for typed fallback behaviour.
/// </summary>
/// <typeparam name="T">The expected return type of the guarded operation.</typeparam>
public sealed class FallbackOptions<T>
{
    /// <summary>
    /// Static fallback value returned when the operation fails.
    /// Mutually exclusive with <see cref="FallbackFactory"/> — factory takes precedence if both are set.
    /// </summary>
    public T? FallbackValue { get; set; }

    /// <summary>
    /// Factory that produces the fallback value from the resilience context.
    /// Allows dynamic fallbacks (e.g. cached response, default DTO).
    /// Takes precedence over <see cref="FallbackValue"/>.
    /// </summary>
    public Func<ResilienceContext, Task<T>>? FallbackFactory { get; set; }

    /// <summary>
    /// Determines whether the fallback should activate for a given exception.
    /// When <c>null</c>, any exception triggers the fallback.
    /// </summary>
    public Func<Exception, bool>? FallbackOnException { get; set; }

    /// <summary>
    /// Determines whether the fallback should activate for a given result value.
    /// Useful when you want to replace a failed-but-non-throwing result (e.g. <see cref="Vali_Mediator.Core.Result.IResult"/>).
    /// When <c>null</c>, result-based activation is disabled.
    /// </summary>
    public Func<T?, bool>? FallbackOnResultPredicate { get; set; }

    /// <summary>
    /// Called when the fallback activates, before returning the fallback value.
    /// Useful for logging, metrics, or alerts.
    /// </summary>
    public Func<ResilienceContext, Exception?, Task>? OnFallback { get; set; }
}
