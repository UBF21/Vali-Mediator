namespace Vali_Mediator.Core.Request;

/// <summary>
/// Marker interface for a request that expects a response.
/// </summary>
/// <typeparam name="TResponse">The type of the response expected from the request.</typeparam>
public interface IRequest<TResponse>
{
}