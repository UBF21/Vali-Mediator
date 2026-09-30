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
    /// Gets the version-independent type name of the response, checked before deserializing.
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
    public bool IsExpired => IsExpiredAt(DateTimeOffset.UtcNow);

    /// <summary>
    /// Gets the SHA-256 fingerprint (hex) of the original request payload, or empty when it was not recorded.
    /// </summary>
    public string RequestFingerprint { get; init; } = string.Empty;

    /// <summary>
    /// Returns whether this entry is expired at <paramref name="now"/>.
    /// </summary>
    /// <param name="now">The instant to evaluate against.</param>
    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAt.HasValue && now > ExpiresAt.Value;
}
