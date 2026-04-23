using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.Notification;

/// <summary>
/// Defines a handler for processing a specific notification.
/// Multiple handlers can subscribe to the same notification, enabling a publish-subscribe pattern.
/// </summary>
/// <typeparam name="TNotification">The type of the notification to handle. Must implement <see cref="INotification"/>.</typeparam>
/// <remarks>
/// Implementations are invoked via <see cref="IValiMediator.Publish{TNotification}(TNotification, CancellationToken)"/> in descending
/// <see cref="Priority"/> order. Handlers with the same priority execute in registration order.
/// </remarks>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Handles the specified notification.
    /// </summary>
    /// <param name="notification">The notification object to process.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task Handle(TNotification notification, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the execution priority of this handler.
    /// Higher values execute first. Defaults to <c>0</c>.
    /// </summary>
    int Priority => 0;
}
