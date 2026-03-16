using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Streaming;

namespace Vali_Mediator.Core.General.Mediator;

/// <summary>
/// Entry point for the Vali-Mediator pipeline.
/// Dispatches requests, notifications, fire-and-forget commands, and streams.
/// </summary>
public interface IValiMediator
{
    /// <summary>
    /// Sends a request through the pipeline to its registered handler.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="request">The request to process.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The handler's response.</returns>
    /// <exception cref="General.Exceptions.HandlerNotFoundException">
    /// Thrown when no handler is registered for <typeparamref name="TResponse"/>.
    /// </exception>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request through the pipeline, returning <c>default</c> instead of throwing
    /// when no handler is registered.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="request">The request to process.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The handler's response, or <c>default(<typeparamref name="TResponse"/>)</c>
    /// if no handler is registered.
    /// </returns>
    Task<TResponse?> SendOrDefault<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to all registered handlers sequentially,
    /// in descending <c>Priority</c> order.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task Publish<TNotification>(TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Publishes a notification to all registered handlers using the specified dispatch strategy.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="strategy">
    /// <see cref="PublishStrategy.Sequential"/>: handlers run one after another (default).<br/>
    /// <see cref="PublishStrategy.Parallel"/>: all handlers run concurrently via <c>Task.WhenAll</c>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task Publish<TNotification>(TNotification notification,
        PublishStrategy strategy,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Dispatches a fire-and-forget command through the pipeline.
    /// No response is returned; use for side effects such as emails, logging, or queuing.
    /// </summary>
    /// <param name="fireAndForget">The command to dispatch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="General.Exceptions.HandlerNotFoundException">
    /// Thrown when no handler is registered for the command type.
    /// </exception>
    Task Send(IFireAndForget fireAndForget, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends multiple requests concurrently and returns all responses in the same order.
    /// Each request runs in its own pipeline. Exceptions from individual requests are
    /// propagated as an <see cref="AggregateException"/> if any fail.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type for every request.</typeparam>
    /// <param name="requests">The requests to dispatch in parallel.</param>
    /// <param name="cancellationToken">A token shared across all requests.</param>
    /// <returns>An array of responses in the same order as <paramref name="requests"/>.</returns>
    Task<TResponse[]> SendAll<TResponse>(IEnumerable<IRequest<TResponse>> requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a streaming request and returns an asynchronous sequence of results.
    /// </summary>
    /// <typeparam name="TResponse">The element type of the stream.</typeparam>
    /// <param name="request">The streaming request to process.</param>
    /// <param name="cancellationToken">A token to cancel iteration.</param>
    /// <returns>An <see cref="IAsyncEnumerable{T}"/> of response elements.</returns>
    /// <remarks>
    /// Pipeline behaviors and processors are not applied to streaming requests.
    /// The stream is lazy — elements are produced on demand as the caller iterates.
    /// </remarks>
    /// <exception cref="General.Exceptions.HandlerNotFoundException">
    /// Thrown when no stream handler is registered for the request type.
    /// </exception>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}
