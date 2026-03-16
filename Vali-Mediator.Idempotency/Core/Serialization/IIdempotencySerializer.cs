namespace Vali_Mediator_Idempotency.Core.Serialization;

/// <summary>
/// Defines the serialization contract used by the idempotency pipeline
/// to persist and restore handler responses.
/// </summary>
public interface IIdempotencySerializer
{
    /// <summary>
    /// Serializes <paramref name="value"/> to a byte array.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>A byte array representing the serialized form of <paramref name="value"/>.</returns>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// Deserializes a byte array back to an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="data">The byte array to deserialize.</param>
    /// <returns>The deserialized instance, or <c>null</c> if deserialization yields no result.</returns>
    T? Deserialize<T>(byte[] data);
}
