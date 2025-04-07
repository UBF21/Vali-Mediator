using Vali_Mediator.Core.General.Base;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Request;

namespace Vali_Mediator.Core.General.Behavior;

/// <summary>
/// Defines a behavior that can be applied to the processing pipeline of a request of type <typeparamref name="TRequest"/> 
/// that returns a response of type <typeparamref name="TResponse"/>.
/// This interface is used by the <see cref="IValiMediator"/> to execute cross-cutting concerns such as logging, validation, or error handling 
/// for requests that produce a result.
/// </summary>
/// <typeparam name="TRequest">
/// The type of the request to process, which must implement <see cref="IRequest{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">
/// The type of the response returned by the request.
/// </typeparam>
/// <remarks>
/// Implementations of this interface can intercept and modify the execution flow of a request before or after it reaches its handler,
/// returning a response of type <typeparamref name="TResponse"/>. This allows chaining multiple behaviors in the pipeline.
/// </remarks>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
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

/// <summary>
/// Defines a behavior that can be applied to the processing pipeline of a dispatch-able request of type <typeparamref name="TRequest"/>.
/// This interface is used by the <see cref="IValiMediator"/> to execute cross-cutting concerns such as logging, validation, or error handling.
/// </summary>
/// <typeparam name="TRequest">
/// The type of the request to process, which must implement <see cref="IDispatch"/>.
/// </typeparam>
/// <remarks>
/// Implementations of this interface can intercept and modify the execution flow of a request before or after it reaches its handler.
/// The <see cref="Handle"/> method forms part of a pipeline, allowing multiple behaviors to be chained together.
/// </remarks>
public interface IPipelineBehavior<in TRequest> where TRequest : IDispatch
{
    /// <summary>
    /// Handles the specified request as part of the processing pipeline.
    /// </summary>
    /// <param name="request">
    /// The request instance to process, which implements <see cref="IDispatch"/>.
    /// </param>
    /// <param name="next">
    /// A delegate representing the next step in the pipeline. Invoking this delegate continues the execution flow.
    /// </param>
    /// <param name="cancellationToken">
    /// A <see cref="CancellationToken"/> that can be used to cancel the pipeline operation.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous execution of the pipeline behavior.
    /// </returns>
    /// <remarks>
    /// Implementations should call <paramref name="next"/> to proceed with the pipeline, unless the behavior intentionally
    /// terminates the flow (e.g., due to validation failure). This method can perform actions before and/or after calling <paramref name="next"/>.
    /// </remarks>
    Task Handle(TRequest request, Func<Task> next, CancellationToken cancellationToken);
}