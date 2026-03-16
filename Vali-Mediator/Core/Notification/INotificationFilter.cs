namespace Vali_Mediator.Core.Notification;

/// <summary>
/// Allows a notification handler to declare whether it should process a given notification.
/// Evaluated before the handler's <c>Handle</c> method is called.
/// </summary>
/// <remarks>
/// Implement this interface on your <see cref="INotificationHandler{TNotification}"/> to
/// conditionally opt-out of handling specific notification instances without throwing an exception.
/// When <see cref="ShouldHandle"/> returns <c>false</c> the handler is silently skipped.
/// </remarks>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationFilter<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Returns <c>true</c> if this handler should process <paramref name="notification"/>;
    /// <c>false</c> to skip it.
    /// </summary>
    bool ShouldHandle(TNotification notification);
}
