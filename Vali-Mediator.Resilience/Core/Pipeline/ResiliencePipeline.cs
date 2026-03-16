using Vali_Mediator.Core.Result;
using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Registry;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>
/// Orchestrates execution of all configured resilience policies in the correct order:
/// Fallback → Chaos → RateLimiter → Timeout → Circuit Breaker → Bulkhead → Retry → Hedge → delegate.
/// </summary>
internal sealed class ResiliencePipeline
{
    private readonly RetryOptions? _retry;
    private readonly CircuitBreakerOptions? _circuitBreaker;
    private readonly TimeoutOptions? _timeout;
    private readonly BulkheadOptions? _bulkhead;
    private readonly HedgeOptions? _hedge;
    private readonly RateLimiterOptions? _rateLimiterOptions;
    private readonly ChaosOptions? _chaos;
    private readonly ICircuitBreakerRegistry _registry;

    // Per-instance semaphore for bulkhead (when not using DI)
    private SemaphoreSlim? _bulkheadSemaphore;

    // Per-instance rate limiter state
    private readonly RateLimiterState? _rateLimiterState;

    internal ResiliencePipeline(
        RetryOptions? retry,
        CircuitBreakerOptions? circuitBreaker,
        TimeoutOptions? timeout,
        BulkheadOptions? bulkhead,
        HedgeOptions? hedge,
        RateLimiterOptions? rateLimiter,
        ChaosOptions? chaos,
        ICircuitBreakerRegistry registry)
    {
        _retry = retry;
        _circuitBreaker = circuitBreaker;
        _timeout = timeout;
        _bulkhead = bulkhead;
        _hedge = hedge;
        _rateLimiterOptions = rateLimiter;
        _chaos = chaos;
        _registry = registry;

        if (_bulkhead != null)
        {
            int initialCount = _bulkhead.MaxConcurrentCalls;
            int maxCount = _bulkhead.MaxConcurrentCalls + _bulkhead.MaxQueuedCalls;
            _bulkheadSemaphore = new SemaphoreSlim(initialCount, maxCount > 0 ? maxCount : initialCount);
        }

        if (_rateLimiterOptions != null)
            _rateLimiterState = new RateLimiterState(_rateLimiterOptions);
    }

    // -----------------------------------------------------------------------
    // Public entry points
    // -----------------------------------------------------------------------

    /// <summary>Executes the operation with typed return value applying all resilience policies.</summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        FallbackOptions<T>? fallback,
        ResilienceContext context,
        CancellationToken cancellationToken = default)
    {
        context.CancellationToken = cancellationToken;

        // Chaos: inject faults before anything else (outermost after fallback)
        Func<CancellationToken, Task<T>> innerOp = _chaos != null
            ? ct => ChaosExecutor.ExecuteAsync(operation, _chaos, ct)
            : operation;

        // RateLimiter: reject fast before touching timeout/circuit breaker
        Func<CancellationToken, Task<T>> rateLimited = _rateLimiterState != null && _rateLimiterOptions != null
            ? ct => RateLimiterExecutor.ExecuteAsync(innerOp, _rateLimiterState, _rateLimiterOptions, context, ct)
            : innerOp;

        // Outer: Fallback wraps everything
        if (fallback != null)
        {
            try
            {
                return await ExecuteWithTimeoutCircuitBulkheadRetryAsync(rateLimited, context, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldActivateFallback(fallback, ex))
            {
                if (fallback.OnFallback != null)
                    await fallback.OnFallback(context, ex).ConfigureAwait(false);

                return await ResolveFallbackValue(fallback, context).ConfigureAwait(false);
            }
        }

        return await ExecuteWithTimeoutCircuitBulkheadRetryAsync(rateLimited, context, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Executes a void operation applying all resilience policies.</summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        ResilienceContext context,
        CancellationToken cancellationToken = default)
    {
        context.CancellationToken = cancellationToken;

        Func<CancellationToken, Task<object?>> wrapped = async ct =>
        {
            await operation(ct).ConfigureAwait(false);
            return (object?)null!;
        };

        Func<CancellationToken, Task<object?>> innerOp = _chaos != null
            ? ct => ChaosExecutor.ExecuteAsync(wrapped, _chaos, ct)
            : wrapped;

        Func<CancellationToken, Task<object?>> rateLimited = _rateLimiterState != null && _rateLimiterOptions != null
            ? ct => RateLimiterExecutor.ExecuteAsync(innerOp, _rateLimiterState, _rateLimiterOptions, context, ct)
            : innerOp;

        await ExecuteWithTimeoutCircuitBulkheadRetryAsync(rateLimited, context, cancellationToken)
            .ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Core execution chain
    // -----------------------------------------------------------------------

    private async Task<T> ExecuteWithTimeoutCircuitBulkheadRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        int maxAttempts = _retry != null ? _retry.MaxRetries + 1 : 1;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            context.AttemptNumber = attempt;
            context.ElapsedTime = DateTimeOffset.UtcNow - started;

            try
            {
                // Innermost: Hedge races parallel calls
                Func<CancellationToken, Task<T>> hedged = _hedge != null
                    ? ct => HedgeExecutor.ExecuteAsync(operation, _hedge, context, ct)
                    : operation;

                T value = await ExecuteWithTimeoutAsync(
                    ct => ExecuteWithCircuitBreakerAsync(
                        ct2 => ExecuteWithBulkheadAsync(hedged, context, ct2),
                        context, ct),
                    context, cancellationToken).ConfigureAwait(false);

                // Result-based retry (no exception thrown but the return value signals failure)
                bool isLastAttempt = attempt >= maxAttempts - 1;
                if (_retry != null && !isLastAttempt && ShouldRetryOnResult(_retry, value))
                {
                    context.ElapsedTime = DateTimeOffset.UtcNow - started;
                    TimeSpan retryDelay = CalculateDelay(_retry, attempt);
                    if (_retry.OnRetry != null)
                        await _retry.OnRetry(context, retryDelay).ConfigureAwait(false);
                    if (retryDelay > TimeSpan.Zero)
                        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return value;
            }
            catch (Exception ex)
            {
                context.LastException = ex;
                context.ElapsedTime = DateTimeOffset.UtcNow - started;

                bool isLastAttempt = attempt >= maxAttempts - 1;

                if (_retry == null || isLastAttempt || !ShouldRetry(_retry, ex, null))
                    throw;

                TimeSpan delay = CalculateDelay(_retry, attempt);
                if (_retry.OnRetry != null)
                    await _retry.OnRetry(context, delay).ConfigureAwait(false);

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        // Unreachable — loop always throws or returns
        throw new InvalidOperationException("Resilience pipeline: unexpected loop exit.");
    }

    // -----------------------------------------------------------------------
    // Timeout
    // -----------------------------------------------------------------------

    private async Task<T> ExecuteWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        if (_timeout == null)
            return await operation(cancellationToken).ConfigureAwait(false);

        using var timeoutCts = new CancellationTokenSource(_timeout.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        switch (_timeout.Strategy)
        {
            case Enums.TimeoutStrategy.Optimistic:
            {
                try
                {
                    return await operation(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    context.ElapsedTime = _timeout.Timeout;
                    if (_timeout.OnTimeout != null)
                        await _timeout.OnTimeout(context).ConfigureAwait(false);
                    throw new TimeoutException($"The operation timed out after {_timeout.Timeout.TotalSeconds:0.##} s.");
                }
            }

            case Enums.TimeoutStrategy.Pessimistic:
            default:
            {
                var operationTask = operation(cancellationToken);
                var delayTask = Task.Delay(_timeout.Timeout, CancellationToken.None);
                var completed = await Task.WhenAny(operationTask, delayTask).ConfigureAwait(false);

                if (completed == delayTask)
                {
                    context.ElapsedTime = _timeout.Timeout;
                    if (_timeout.OnTimeout != null)
                        await _timeout.OnTimeout(context).ConfigureAwait(false);
                    throw new TimeoutException($"The operation timed out after {_timeout.Timeout.TotalSeconds:0.##} s (pessimistic).");
                }

                return await operationTask.ConfigureAwait(false);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Circuit Breaker
    // -----------------------------------------------------------------------

    private async Task<T> ExecuteWithCircuitBreakerAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        if (_circuitBreaker == null)
            return await operation(cancellationToken).ConfigureAwait(false);

        var cbState = _registry.GetOrCreate(_circuitBreaker.CircuitKey, _circuitBreaker);

        if (!cbState.CanExecute())
        {
            throw new CircuitOpenException(_circuitBreaker.CircuitKey, cbState.RetryAfter());
        }

        // HalfOpen transition callback
        if (cbState.State == Enums.CircuitState.HalfOpen && _circuitBreaker.OnHalfOpen != null)
            await _circuitBreaker.OnHalfOpen(context).ConfigureAwait(false);

        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);

            // Check if the result itself signals failure (e.g. IResult.IsFailure)
            bool resultIsFailure = result is IResult ir && ir.IsFailure;

            if (resultIsFailure)
                cbState.RecordFailure();
            else
                cbState.RecordSuccess();

            if (cbState.State == Enums.CircuitState.Closed && _circuitBreaker.OnClose != null)
            {
                // Only fire OnClose if we just transitioned (i.e. were HalfOpen before this call)
                // The state machine handles this internally; fire best-effort here
                _ = _circuitBreaker.OnClose(context);
            }

            return result;
        }
        catch (Exception ex) when (ex is not CircuitOpenException)
        {
            var previousState = cbState.State;
            cbState.RecordFailure();

            if (cbState.State == Enums.CircuitState.Open &&
                previousState != Enums.CircuitState.Open &&
                _circuitBreaker.OnOpen != null)
            {
                await _circuitBreaker.OnOpen(context, ex).ConfigureAwait(false);
            }

            throw;
        }
    }

    // -----------------------------------------------------------------------
    // Bulkhead
    // -----------------------------------------------------------------------

    private async Task<T> ExecuteWithBulkheadAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        if (_bulkhead == null || _bulkheadSemaphore == null)
            return await operation(cancellationToken).ConfigureAwait(false);

        bool acquired;
        if (_bulkhead.QueueTimeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            acquired = await _bulkheadSemaphore.WaitAsync(0).ConfigureAwait(false);
        }
        else
        {
            acquired = await _bulkheadSemaphore.WaitAsync(_bulkhead.QueueTimeout, cancellationToken).ConfigureAwait(false);
        }

        if (!acquired)
        {
            if (_bulkhead.OnRejected != null)
                await _bulkhead.OnRejected(context).ConfigureAwait(false);

            throw new BulkheadRejectedException(_bulkhead.MaxConcurrentCalls, _bulkhead.MaxQueuedCalls);
        }

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _bulkheadSemaphore.Release();
        }
    }

    // -----------------------------------------------------------------------
    // Retry helpers
    // -----------------------------------------------------------------------

    private static bool ShouldRetry(RetryOptions options, Exception ex, object? result)
    {
        // Result predicate (non-exception path)
        if (result != null && options.RetryOnResultPredicate != null)
            return options.RetryOnResultPredicate(result);

        // IResult failure type check
        if (ex == null && result is IResult ir && options.RetryOnErrorTypes.Count > 0)
            return options.RetryOnErrorTypes.Contains(ir.ErrorType);

        if (ex is OperationCanceledException)
            return false;

        // Custom predicate
        if (options.RetryOnPredicate != null)
            return options.RetryOnPredicate(ex!);

        // Specific exception types
        if (options.RetryOnExceptions.Count > 0)
        {
            var exType = ex!.GetType();
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

    private static TimeSpan CalculateDelay(RetryOptions options, int attempt)
    {
        TimeSpan delay;

        switch (options.BackoffType)
        {
            case Enums.BackoffType.Fixed:
                delay = options.InitialDelay;
                break;

            case Enums.BackoffType.Linear:
                delay = TimeSpan.FromMilliseconds(options.InitialDelay.TotalMilliseconds * (attempt + 1));
                break;

            case Enums.BackoffType.Exponential:
                delay = TimeSpan.FromMilliseconds(
                    options.InitialDelay.TotalMilliseconds * Math.Pow(options.Multiplier, attempt));
                break;

            case Enums.BackoffType.ExponentialWithJitter:
            {
                double baseMs = options.InitialDelay.TotalMilliseconds * Math.Pow(options.Multiplier, attempt);
                double jitter = baseMs * 0.2 * (Random.Shared.NextDouble() * 2 - 1); // ±20%
                delay = TimeSpan.FromMilliseconds(baseMs + jitter);
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

        // Clamp to MaxDelay
        if (delay > options.MaxDelay) delay = options.MaxDelay;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        return delay;
    }

    // -----------------------------------------------------------------------
    // Fallback helpers
    // -----------------------------------------------------------------------

    private static bool ShouldActivateFallback<T>(FallbackOptions<T> fallback, Exception ex)
    {
        if (fallback.FallbackOnException != null)
            return fallback.FallbackOnException(ex);
        return true;
    }

    private static async Task<T> ResolveFallbackValue<T>(FallbackOptions<T> fallback, ResilienceContext context)
    {
        if (fallback.FallbackFactory != null)
            return await fallback.FallbackFactory(context).ConfigureAwait(false);

        return fallback.FallbackValue!;
    }
}
