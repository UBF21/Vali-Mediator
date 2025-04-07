using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.Notification;

/// <summary>
/// Defines a handler for processing a specific notification.
/// </summary>
/// <typeparam name="TNotification">The type of the notification to handle. Must implement <see cref="INotification"/>.</typeparam>
/// <remarks>
/// Implementations of this interface are responsible for processing notifications of type <typeparamref name="TNotification"/> 
/// dispatched via the <see cref="IValiMediator.Publish{TNotification}"/> method. Multiple handlers can subscribe to the same 
/// notification, enabling a publish-subscribe pattern within the Mediator framework.
/// </remarks>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Handles the specified notification.
    /// </summary>
    /// <param name="notification">The notification object to process.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}