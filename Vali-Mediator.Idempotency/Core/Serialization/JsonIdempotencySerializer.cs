using System.Text.Json;

namespace Vali_Mediator_Idempotency.Core.Serialization;

/// <summary>
/// Default <see cref="IIdempotencySerializer"/> implementation backed by <c>System.Text.Json</c>.
/// </summary>
public sealed class JsonIdempotencySerializer : IIdempotencySerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, DefaultOptions);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(byte[] data)
    {
        if (data is null || data.Length == 0)
            return default;

        return JsonSerializer.Deserialize<T>(data, DefaultOptions);
    }
}
