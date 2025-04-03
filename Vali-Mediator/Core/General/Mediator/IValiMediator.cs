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
}