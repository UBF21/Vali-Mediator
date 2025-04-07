using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.Request;

/// <summary>
/// Defines a handler for processing a specific request and returning a response.
/// </summary>
/// <typeparam name="TRequest">The type of the request to handle. Must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by handling the request.</typeparam>
/// <remarks>
/// Implementations of this interface are responsible for processing requests of type <typeparamref name="TRequest"/> 
/// dispatched via the <see cref="IValiMediator.Send{TResponse}"/> method and producing a response of type <typeparamref name="TResponse"/>.
/// This enables a request-response pattern within the Mediator framework, where each request is handled by a single handler.
/// </remarks>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the specified request and produces a response.
    /// </summary>
    /// <param name="request">The request object to process.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the response of type <typeparamref name="TResponse"/>.</returns>
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}