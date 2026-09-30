using Vali_Mediator.Core.Result;
using Vali_Mediator_Idempotency.Core.Serialization;
using Xunit;

namespace Vali_Mediator_Idempotency.Tests;

public class ResultJsonRoundTripTests
{
    private readonly JsonIdempotencySerializer _serializer = new JsonIdempotencySerializer();

    [Fact]
    public void ResultOfT_Ok_RoundTrips()
    {
        var back = _serializer.Deserialize<Result<string>>(_serializer.Serialize(Result<string>.Ok("x")));
        Assert.True(back.IsSuccess);
        Assert.Equal("x", back.Value);
    }

    [Fact]
    public void ResultOfT_Fail_RoundTrips()
    {
        var back = _serializer.Deserialize<Result<string>>(_serializer.Serialize(Result<string>.Fail("err", ErrorType.NotFound)));
        Assert.False(back.IsSuccess);
        Assert.Equal("err", back.Error);
        Assert.Equal(ErrorType.NotFound, back.ErrorType);
    }

    [Fact]
    public void Result_Ok_RoundTrips()
    {
        var back = _serializer.Deserialize<Result>(_serializer.Serialize(Result.Ok()));
        Assert.True(back.IsSuccess);
    }

    [Fact]
    public void Result_Fail_RoundTrips()
    {
        var back = _serializer.Deserialize<Result>(_serializer.Serialize(Result.Fail("err", ErrorType.Conflict)));
        Assert.False(back.IsSuccess);
        Assert.Equal("err", back.Error);
        Assert.Equal(ErrorType.Conflict, back.ErrorType);
    }
}
