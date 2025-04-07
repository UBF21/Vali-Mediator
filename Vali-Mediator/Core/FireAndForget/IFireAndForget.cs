using Vali_Mediator.Core.General.Base;

namespace Vali_Mediator.Core.FireAndForget;

/// <summary>
/// Defines a marker interface for fire-and-forget commands that perform actions without returning a response.
/// This interface inherits from <see cref="IDispatch"/> and is used to represent operations dispatched via the <see cref="IMediator"/>.
/// </summary>
/// <remarks>
/// Implementations of this interface are typically handled by an <see cref="IFireAndForgetHandler{TRequest}"/> and are executed asynchronously
/// without expecting a result. This is useful for commands that modify state or trigger side effects.
/// </remarks>
public interface IFireAndForget : IDispatch
{
}