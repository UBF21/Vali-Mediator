using Vali_Mediator.Core.General.Base;
using Vali_Mediator.Core.Request;

namespace Vali_Mediator.Core.General.Behavior;

/// <summary>
/// Defines a pipeline behavior that can intercept and process a request before or after it reaches its handler.
/// </summary>
/// <typeparam name="TRequest">The type of the request being processed. Must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the request handler.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
{
    /// <summary>
    /// Handles the request by performing additional processing before or after invoking the next behavior or handler in the pipeline.
    /// </summary>
    /// <param name="request">The request object to be processed.</param>
    /// <param name="next">A delegate representing the next step in the pipeline, which could be another behavior or the final handler.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the response of type <typeparamref name="TResponse"/>.</returns>
    Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken);
}

public interface IPipelineBehavior<in TRequest> where TRequest : IDispatch
{
    Task Handle(TRequest request, Func<Task> next, CancellationToken cancellationToken);
}