namespace Vali_Mediator.Core.Notification;

/// <summary>
/// Defines a handler for processing a specific notification.
/// </summary>
/// <typeparam name="TNotification">The type of the notification to handle. Must implement <see cref="INotification"/>.</typeparam>
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