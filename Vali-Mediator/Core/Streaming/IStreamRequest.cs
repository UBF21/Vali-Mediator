namespace Vali_Mediator.Core.Streaming;

/// <summary>
/// Marker interface for a streaming request that produces multiple results of type
/// <typeparamref name="TResponse"/> via <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
/// <typeparam name="TResponse">The element type of the stream.</typeparam>
/// <remarks>
/// Handled by a single <see cref="IStreamRequestHandler{TRequest,TResponse}"/> and dispatched
/// via <c>IValiMediator.CreateStream</c>. Pipeline behaviors and processors are not applied
/// to streaming requests; implement cross-cutting logic directly in the handler or via
/// the <see cref="IAsyncEnumerable{T}"/> consumer (e.g., using <c>await foreach</c>).
/// </remarks>
public interface IStreamRequest<TResponse>
{
}
