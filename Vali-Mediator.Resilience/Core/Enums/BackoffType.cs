namespace Vali_Mediator_Resilience.Core.Enums;

/// <summary>
/// Controls how the delay between retry attempts is calculated.
/// </summary>
public enum BackoffType
{
    /// <summary>Every retry waits exactly <c>InitialDelay</c>.</summary>
    Fixed = 0,

    /// <summary>Delay grows linearly: attempt × <c>InitialDelay</c>.</summary>
    Linear = 1,

    /// <summary>Delay doubles on each attempt: <c>InitialDelay</c> × 2^(attempt−1).</summary>
    Exponential = 2,

    /// <summary>
    /// Exponential backoff with ±20% random jitter to prevent thundering-herd effects.
    /// Recommended for distributed systems and external API calls.
    /// </summary>
    ExponentialWithJitter = 3,

    /// <summary>
    /// User-supplied delegate via <see cref="Vali_Mediator_Resilience.Core.Options.RetryOptions.CustomDelayFactory"/>.
    /// </summary>
    Custom = 4
}
