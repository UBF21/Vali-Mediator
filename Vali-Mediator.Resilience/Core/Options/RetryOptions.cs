using Vali_Mediator.Core.Result;
using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Enums;

namespace Vali_Mediator_Resilience.Core.Options;

/// <summary>
/// Configuration for retry behaviour.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>Maximum number of retry attempts (not counting the initial call). Default: 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Controls how the inter-attempt delay is calculated. Default: <see cref="BackoffType.ExponentialWithJitter"/>.</summary>
    public BackoffType BackoffType { get; set; } = BackoffType.ExponentialWithJitter;

    /// <summary>
    /// Base delay for the first retry. Actual delay depends on <see cref="BackoffType"/>.
    /// Default: 200 ms.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Upper bound applied after backoff/jitter calculations.
    /// Default: 30 s.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Multiplier applied to the delay on each attempt when <see cref="BackoffType"/> is
    /// <see cref="BackoffType.Exponential"/> or <see cref="BackoffType.ExponentialWithJitter"/>.
    /// Default: 2.0.
    /// </summary>
    public double Multiplier { get; set; } = 2.0;

    /// <summary>
    /// Exception types that should trigger a retry.
    /// If empty (and <see cref="RetryOnPredicate"/> is null), ALL exceptions trigger a retry.
    /// </summary>
    public List<Type> RetryOnExceptions { get; set; } = new List<Type>();

    /// <summary>
    /// Additional predicate evaluated against the thrown exception.
    /// When set, an exception retries only when this predicate returns <c>true</c>.
    /// </summary>
    public Func<Exception, bool>? RetryOnPredicate { get; set; }

    /// <summary>
    /// When the operation returns a <see cref="IResult"/>, retry if its
    /// <see cref="IResult.ErrorType"/> is in this set.
    /// Complements exception-based retry — either condition alone is sufficient.
    /// </summary>
    public HashSet<ErrorType> RetryOnErrorTypes { get; set; } = new HashSet<ErrorType>();

    /// <summary>
    /// Predicate evaluated against the raw return value. Useful for non-Result returns
    /// (e.g. an HTTP response object). If it returns <c>true</c>, the call is retried.
    /// </summary>
    public Func<object?, bool>? RetryOnResultPredicate { get; set; }

    /// <summary>
    /// Called after each failed attempt, before sleeping.
    /// <c>context.AttemptNumber</c> is the attempt that just failed (0-based).
    /// <c>delay</c> is the sleep duration that will follow.
    /// </summary>
    public Func<ResilienceContext, TimeSpan, Task>? OnRetry { get; set; }

    /// <summary>
    /// Required when <see cref="BackoffType"/> is <see cref="BackoffType.Custom"/>.
    /// Receives the 0-based attempt index and must return the delay before the next attempt.
    /// </summary>
    public Func<int, TimeSpan>? CustomDelayFactory { get; set; }
}
