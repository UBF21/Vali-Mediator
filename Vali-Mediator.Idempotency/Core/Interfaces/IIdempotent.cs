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
    /// <c>null</c> lets the store apply its default expiration (24 hours for the in-memory store).
    /// </summary>
    TimeSpan? Expiration { get; }

    /// <summary>
    /// Gets an optional scope (user, tenant, client id) that isolates keys: the same
    /// <see cref="IdempotencyKey"/> under different scopes never shares a stored response.
    /// Return the caller identity for any request whose key is client-supplied. Default: <c>null</c> (no scope).
    /// </summary>
    string? IdempotencyScope => null;
}
