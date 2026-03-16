namespace Vali_Mediator_Idempotency.Core.Models;

/// <summary>
/// Represents a stored idempotency record containing the serialized response
/// for a previously executed request.
/// </summary>
public sealed class IdempotencyEntry
{
    /// <summary>
    /// Gets the idempotency key that identifies this entry.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets the JSON-serialized bytes of the handler response.
    /// </summary>
    public byte[] SerializedResponse { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Gets the assembly-qualified type name of the response, used for deserialization.
    /// </summary>
    public string ResponseTypeName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp at which this entry was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the UTC timestamp at which this entry expires, or <c>null</c> if it never expires.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Gets a value indicating whether this entry has passed its expiry time.
    /// Returns <c>false</c> for entries that have no expiry (<see cref="ExpiresAt"/> is <c>null</c>).
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTimeOffset.UtcNow > ExpiresAt.Value;
}
