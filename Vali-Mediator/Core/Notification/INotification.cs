using Vali_Mediator.Core.General.Base;
using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.Notification;

/// <summary>
/// Marker interface for a notification that can be published to multiple handlers.
/// </summary>
/// <remarks>
/// This interface inherits from <see cref="IDispatch"/> and is used to represent events or notifications that are dispatched 
/// via the <see cref="IValiMediator.Publish{TNotification}"/> method. Implementations of this interface can be processed by 
/// multiple <see cref="INotificationHandler{TNotification}"/> instances, enabling a publish-subscribe pattern.
/// </remarks>
public interface INotification : IDispatch
{
}