using Vali_Mediator.Core.Request;

namespace Vali_Mediator.Core.General.Behavior;

/// <summary>
/// Pipeline behavior that enforces the timeout declared by <see cref="IHasTimeout"/> requests.
/// Uses a linked <see cref="CancellationTokenSource"/> combining the request's declared
/// <see cref="IHasTimeout.Timeout"/> with the caller's token — whichever fires first wins.
/// </summary>
/// <remarks>
/// Register via <c>config.AddTimeoutBehavior()</c> in <c>AddValiMediator</c>.
/// The behavior is a no-op for requests that do not implement <see cref="IHasTimeout"/>.
/// </remarks>
public sealed class TimeoutBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        Func<CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        if (request is not IHasTimeout hasTimeout)
            return await next(cancellationToken).ConfigureAwait(false);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(hasTimeout.Timeout);

        var operation = next(linkedCts.Token);
        try
        {
            // WaitAsync keeps the timeout effective for handlers that ignore the token.
            return await operation.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && linkedCts.IsCancellationRequested)
        {
            // Drivers often turn cancellation into their own exception type: it is still a timeout.
            ObserveLateFailure(operation);
            throw new TimeoutException(
                $"Request '{typeof(TRequest).Name}' timed out after {hasTimeout.Timeout.TotalSeconds:0.##} s.", ex);
        }
        catch (OperationCanceledException)
        {
            ObserveLateFailure(operation);
            throw;
        }
    }

    // A handler that ignores its token keeps running after we stop waiting; its later failure must not go unobserved.
    private static void ObserveLateFailure(Task operation)
    {
        if (!operation.IsCompleted)
            operation.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}
