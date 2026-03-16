using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.Request;

/// <summary>
/// Marker interface for a request that expects a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The type of the response expected from the request.</typeparam>
/// <remarks>
/// Dispatched via <see cref="IValiMediator.Send{TResponse}"/> and processed by a single
/// <see cref="IRequestHandler{TRequest,TResponse}"/>.
/// </remarks>
public interface IRequest<TResponse>
{
}

/// <summary>
/// Marker interface for a request that produces no result.
/// Shortcut for <see cref="IRequest{Unit}"/>.
/// </summary>
public interface IRequest : IRequest<Unit>
{
}