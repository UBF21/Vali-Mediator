using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.Request;

/// <summary>
/// Marker interface for a request that expects a response.
/// </summary>
/// <typeparam name="TResponse">The type of the response expected from the request.</typeparam>
/// <remarks>
/// This interface is used to define requests that are dispatched via the <see cref="IValiMediator.Send{TResponse}"/> method 
/// and processed by an <see cref="IRequestHandler{TRequest, TResponse}"/> to produce a result of type <typeparamref name="TResponse"/>.
/// It enables a request-response pattern within the Mediator framework.
/// </remarks>
public interface IRequest<TResponse>
{
}