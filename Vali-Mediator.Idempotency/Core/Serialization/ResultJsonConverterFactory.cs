using System.Text.Json;
using System.Text.Json.Serialization;
using Vali_Mediator.Core.Result;

namespace Vali_Mediator_Idempotency.Core.Serialization;

/// <summary>
/// Teaches <c>System.Text.Json</c> to round-trip <see cref="Result"/> and <see cref="Result{T}"/>,
/// whose constructors are private and whose properties are read-only.
/// </summary>
public sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(Result)
           || (typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));

    /// <inheritdoc />
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeToConvert == typeof(Result)
            ? typeof(ResultConverter)
            : typeof(ResultOfTConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }

    private sealed class ResultDto
    {
        public bool IsSuccess { get; set; }
        public string? Error { get; set; }
        public ErrorType ErrorType { get; set; }
    }

    private sealed class ResultOfTDto<T>
    {
        public bool IsSuccess { get; set; }
        public T? Value { get; set; }
        public string? Error { get; set; }
        public ErrorType ErrorType { get; set; }
        public Dictionary<string, List<string>>? ValidationErrors { get; set; }
    }

    private sealed class ResultConverter : JsonConverter<Result>
    {
        public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = JsonSerializer.Deserialize<ResultDto>(ref reader, options)!;
            return dto.IsSuccess ? Result.Ok() : Result.Fail(dto.Error ?? string.Empty, dto.ErrorType);
        }

        public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer,
                new ResultDto { IsSuccess = value.IsSuccess, Error = value.Error, ErrorType = value.ErrorType },
                options);
    }

    private sealed class ResultOfTConverter<T> : JsonConverter<Result<T>>
    {
        public override Result<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = JsonSerializer.Deserialize<ResultOfTDto<T>>(ref reader, options)!;
            if (dto.IsSuccess) return Result<T>.Ok(dto.Value!);
            if (dto.ValidationErrors is not null) return Result<T>.Fail(dto.ValidationErrors, dto.ErrorType);
            return Result<T>.Fail(dto.Error ?? string.Empty, dto.ErrorType);
        }

        public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer,
                new ResultOfTDto<T>
                {
                    IsSuccess = value.IsSuccess,
                    Value = value.Value,
                    Error = value.Error,
                    ErrorType = value.ErrorType,
                    ValidationErrors = value.ValidationErrors?.ToDictionary(k => k.Key, k => k.Value.ToList())
                },
                options);
    }
}
