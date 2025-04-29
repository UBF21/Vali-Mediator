using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Base;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;

namespace Vali_Mediator.Core.Processors;

/// <summary>
/// Defines a preprocessor that executes before a dispatch-able type is handled.
/// </summary>
/// <typeparam name="TDispatch">The type of the dispatch-able object, which must implement <see cref="IDispatch"/> (e.g., <see cref="INotification"/> or <see cref="IFireAndForget"/>).</typeparam>
public interface IPreProcessor<TDispatch> where TDispatch : IDispatch
{
    /// <summary>
    /// Performs an additional task before the dispatch-able object is handled.
    /// </summary>
    /// <param name="dispatch">The dispatch-able object to process.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    void Process(TDispatch dispatch, CancellationToken cancellationToken);
}

/// <summary>
/// Defines a preprocessor that executes before a request is handled.
/// </summary>
/// <typeparam name="TRequest">The type of the request, which must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface IPreProcessor<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Performs an additional task before the request is handled.
    /// </summary>
    /// <param name="request">The request object to process.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    void Process(TRequest request, CancellationToken cancellationToken);
}
