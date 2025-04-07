using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;

namespace Vali_Mediator.Core.General.Base;

/// <summary>
/// Defines a marker interface for types that can be dispatched by the <see cref="IValiMediator"/>.
/// </summary>
/// <remarks>
/// This interface serves as a common base for dispatch-able types such as <see cref="IRequest{TResponse}"/>,
/// <see cref="IFireAndForget"/>, and <see cref="INotification"/>. It provides a way to identify and group
/// objects that can be processed through the Mediator pattern.
/// </remarks>
public interface IDispatch
{
}