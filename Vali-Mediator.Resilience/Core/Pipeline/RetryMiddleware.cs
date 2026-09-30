using Vali_Mediator.Core.Result;
using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>
/// Retries the inner chain. Also runs when no retry is configured (single attempt) so that
/// <see cref="ResilienceContext.AttemptNumber"/>, <see cref="ResilienceContext.ElapsedTime"/> and
/// <see cref="ResilienceContext.LastException"/> are always maintained.
/// </summary>
internal sealed class RetryMiddleware : IResilienceMiddleware
{
    private readonly RetryOptions? _options;

    internal RetryMiddleware(RetryOptions? options)
    {
        _options = options;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> next,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        int maxAttempts = _options != null ? _options.MaxRetries + 1 : 1;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            context.AttemptNumber = attempt;
            context.ElapsedTime = DateTimeOffset.UtcNow - started;
            bool isLastAttempt = attempt >= maxAttempts - 1;

            T value;
            try
            {
                value = await next(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                context.LastException = ex;
                context.ElapsedTime = DateTimeOffset.UtcNow - started;

                if (_options == null || isLastAttempt || !ShouldRetry(_options, ex))
                    throw;

                await WaitBeforeRetryAsync(_options, context, attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // Result-based retry: no exception, but the returned value signals failure.
            if (_options == null || isLastAttempt || !ShouldRetryOnResult(_options, value))
                return value;

            context.ElapsedTime = DateTimeOffset.UtcNow - started;
            await WaitBeforeRetryAsync(_options, context, attempt, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Resilience pipeline: unexpected loop exit.");
    }

    private static async Task WaitBeforeRetryAsync(
        RetryOptions options, ResilienceContext context, int attempt, CancellationToken cancellationToken)
    {
        TimeSpan delay = CalculateDelay(options, attempt);
        if (options.OnRetry != null)
            await options.OnRetry(context, delay).ConfigureAwait(false);
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    internal static bool ShouldRetry(RetryOptions options, Exception ex)
    {
        if (ex is OperationCanceledException)
            return false;

        if (options.RetryOnPredicate != null)
            return options.RetryOnPredicate(ex);

        if (options.RetryOnExceptions.Count > 0)
        {
            var exType = ex.GetType();
            foreach (var t in options.RetryOnExceptions)
                if (t.IsAssignableFrom(exType)) return true;
            return false;
        }

        return true; // default: retry on any exception
    }

    internal static bool ShouldRetryOnResult(RetryOptions options, object? result)
    {
        if (result is IResult ir && options.RetryOnErrorTypes.Count > 0)
            return ir.IsFailure && options.RetryOnErrorTypes.Contains(ir.ErrorType);

        if (options.RetryOnResultPredicate != null)
            return options.RetryOnResultPredicate(result);

        return false;
    }

    internal static TimeSpan CalculateDelay(RetryOptions options, int attempt)
    {
        TimeSpan delay;

        switch (options.BackoffType)
        {
            case Enums.BackoffType.Fixed:
                delay = options.InitialDelay;
                break;

            case Enums.BackoffType.Linear:
                delay = ClampedDelay(options.InitialDelay.TotalMilliseconds * (attempt + 1), options.MaxDelay);
                break;

            case Enums.BackoffType.Exponential:
                delay = ClampedDelay(
                    options.InitialDelay.TotalMilliseconds * Math.Pow(options.Multiplier, attempt), options.MaxDelay);
                break;

            case Enums.BackoffType.ExponentialWithJitter:
            {
                double baseMs = options.InitialDelay.TotalMilliseconds * Math.Pow(options.Multiplier, attempt);
                double jitter = baseMs * 0.2 * (Random.Shared.NextDouble() * 2 - 1); // ±20%
                delay = ClampedDelay(baseMs + jitter, options.MaxDelay);
                break;
            }

            case Enums.BackoffType.Custom:
                if (options.CustomDelayFactory == null)
                    throw new InvalidOperationException(
                        "BackoffType.Custom requires RetryOptions.CustomDelayFactory to be set.");
                delay = options.CustomDelayFactory(attempt);
                break;

            default:
                delay = options.InitialDelay;
                break;
        }

        if (delay > options.MaxDelay) delay = options.MaxDelay;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        return delay;
    }

    // TimeSpan.FromMilliseconds throws on overflow/NaN/infinity, so clamp in double space first.
    private static TimeSpan ClampedDelay(double milliseconds, TimeSpan maxDelay)
    {
        if (double.IsNaN(milliseconds) || milliseconds <= 0) return TimeSpan.Zero;
        return milliseconds >= maxDelay.TotalMilliseconds ? maxDelay : TimeSpan.FromMilliseconds(milliseconds);
    }
}
