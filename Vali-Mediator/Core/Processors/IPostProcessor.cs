using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Base;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;

namespace Vali_Mediator.Core.Processors;

/// <summary>
/// Defines a postprocessor that executes after a dispatch-able type is handled.
/// </summary>
/// <typeparam name="TDispatch">The type of the dispatch-able object, which must implement <see cref="IDispatch"/> (e.g., <see cref="INotification"/> or <see cref="IFireAndForget"/>).</typeparam>
public interface IPostProcessor<TDispatch> where TDispatch : IDispatch
{
    /// <summary>
    /// Performs an additional task after the dispatch-able object has been handled.
    /// </summary>
    /// <param name="dispatch">The dispatch-able object that was processed.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    void Process(TDispatch dispatch, CancellationToken cancellationToken);
}

/// <summary>
/// Defines a postprocessor that executes after a request is handled.
/// </summary>
/// <typeparam name="TRequest">The type of the request, which must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface IPostProcessor<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Performs an additional task after the request has been handled, using the response for inspection.
    /// </summary>
    /// <param name="request">The request object that was processed.</param>
    /// <param name="response">The response returned by the handler.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    void Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}