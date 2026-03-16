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
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        if (request is not IHasTimeout hasTimeout)
            return await next().ConfigureAwait(false);

        using var timeoutCts = new CancellationTokenSource(hasTimeout.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        var operationTask = next();
        var delayTask = Task.Delay(hasTimeout.Timeout, CancellationToken.None);

        var completed = await Task.WhenAny(operationTask, delayTask).ConfigureAwait(false);

        if (completed == delayTask && !operationTask.IsCompleted)
        {
            throw new TimeoutException(
                $"Request '{typeof(TRequest).Name}' timed out after {hasTimeout.Timeout.TotalSeconds:0.##} s.");
        }

        return await operationTask.ConfigureAwait(false);
    }
}
