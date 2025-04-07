using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;

namespace Vali_Mediator.Core.General.Mediator;

/// <summary>
/// Defines a mediator for sending requests and publishing notifications within the application.
/// </summary>
public interface IValiMediator
{
    /// <summary>
    /// Sends a request to be processed by its corresponding handler, optionally through a pipeline of behaviors.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response expected from the request.</typeparam>
    /// <param name="request">The request object to be processed.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the response of type <typeparamref name="TResponse"/>.</returns>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to all registered handlers for processing.
    /// </summary>
    /// <typeparam name="TNotification">The type of the notification to publish. Must implement <see cref="INotification"/>.</typeparam>
    /// <param name="notification">The notification object to be processed.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
    
    /// <summary>
    /// Sends a fire-and-forget command to be processed asynchronously by its corresponding handler, optionally through a pipeline of behaviors.
    /// </summary>
    /// <param name="fireAndForget">
    /// The fire-and-forget command to dispatch, which must implement <see cref="IFireAndForget"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests. Defaults to <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous operation of processing the command.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="fireAndForget"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no handler is registered for the specified <paramref name="fireAndForget"/> type.
    /// </exception>
    /// <remarks>
    /// This method dispatches the command to an <see cref="IFireAndForgetHandler{TFireAndForget}"/> registered with the Mediator.
    /// It is designed for operations that modify state or trigger side effects without returning a response, optionally processed through a pipeline.
    /// </remarks>
    Task Send(IFireAndForget fireAndForget, CancellationToken cancellationToken = default);

}