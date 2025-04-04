using Vali_Mediator.Core.General.Base;

namespace Vali_Mediator.Core.Notification;

/// <summary>
/// Marker interface for a notification that can be published to multiple handlers.
/// </summary>
public interface INotification : IDispatch
{
}