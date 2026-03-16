namespace Vali_Mediator_Idempotency.Core.Interfaces;

/// <summary>
/// Implemented by an <c>IRequest&lt;TResponse&gt;</c> to opt into the idempotency pipeline.
/// </summary>
/// <remarks>
/// When a request implements <see cref="IIdempotent"/>, the <c>IdempotencyBehavior</c>
/// intercepts the pipeline: if a stored response exists for <see cref="IdempotencyKey"/>
/// (and it has not expired), the stored result is returned immediately without invoking the handler.
/// </remarks>
public interface IIdempotent
{
    /// <summary>
    /// Gets the unique key that identifies this request for idempotency purposes.
    /// </summary>
    string IdempotencyKey { get; }

    /// <summary>
    /// Gets the duration for which the stored response should be retained.
    /// <c>null</c> means the entry is kept indefinitely (until the store is cleared or the entry is evicted).
    /// </summary>
    TimeSpan? Expiration { get; }
}
