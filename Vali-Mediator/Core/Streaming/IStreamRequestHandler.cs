namespace Vali_Mediator.Core.Streaming;

/// <summary>
/// Defines a handler for a streaming request of type <typeparamref name="TRequest"/>
/// that produces an asynchronous sequence of <typeparamref name="TResponse"/> elements.
/// </summary>
/// <typeparam name="TRequest">The streaming request type. Must implement <see cref="IStreamRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The element type yielded by the stream.</typeparam>
public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>
    /// Handles the streaming request and returns an asynchronous sequence of results.
    /// </summary>
    /// <param name="request">The streaming request to process.</param>
    /// <param name="cancellationToken">A token to cancel the stream iteration.</param>
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
