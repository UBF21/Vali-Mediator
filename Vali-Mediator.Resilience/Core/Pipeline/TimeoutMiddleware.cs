using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>Enforces a per-attempt time limit (optimistic: cooperative cancellation; pessimistic: stop waiting).</summary>
internal sealed class TimeoutMiddleware : IResilienceMiddleware
{
    private readonly TimeoutOptions _options;

    internal TimeoutMiddleware(TimeoutOptions options)
    {
        _options = options;
    }

    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> next,
        ResilienceContext context,
        CancellationToken cancellationToken)
        => _options.Strategy == TimeoutStrategy.Optimistic
            ? ExecuteOptimisticAsync(next, context, cancellationToken)
            : ExecutePessimisticAsync(next, context, cancellationToken);

    private async Task<T> ExecuteOptimisticAsync<T>(
        Func<CancellationToken, Task<T>> next, ResilienceContext context, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(_options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            return await next(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            context.ElapsedTime = _options.Timeout;
            if (_options.OnTimeout != null)
                await _options.OnTimeout(context).ConfigureAwait(false);
            throw new TimeoutException($"The operation timed out after {_options.Timeout.TotalSeconds:0.##} s.");
        }
    }

    private async Task<T> ExecutePessimisticAsync<T>(
        Func<CancellationToken, Task<T>> next, ResilienceContext context, CancellationToken cancellationToken)
    {
        var operationTask = next(cancellationToken);
        using var delayCts = new CancellationTokenSource();
        var delayTask = Task.Delay(_options.Timeout, delayCts.Token);
        var completed = await Task.WhenAny(operationTask, delayTask).ConfigureAwait(false);

        if (completed != operationTask)
        {
            context.ElapsedTime = _options.Timeout;
            if (_options.OnTimeout != null)
                await _options.OnTimeout(context).ConfigureAwait(false);
            throw new TimeoutException($"The operation timed out after {_options.Timeout.TotalSeconds:0.##} s (pessimistic).");
        }

        delayCts.Cancel();
        return await operationTask.ConfigureAwait(false);
    }
}
