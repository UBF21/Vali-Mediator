using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Vali_Mediator_Idempotency.Core.Interfaces;
using Vali_Mediator_Idempotency.Core.Serialization;
using Vali_Mediator_Idempotency.Core.Store;
using Vali_Mediator_Idempotency.Pipeline;
using Xunit;

namespace Vali_Mediator_Idempotency.Tests;

public class FailedResultIdempotencyTests
{
    private sealed class ResultRequest : IRequest<Result<string>>, IIdempotent
    {
        public string IdempotencyKey { get; init; } = string.Empty;
        public TimeSpan? Expiration { get; init; }
    }

    private static IdempotencyBehavior<ResultRequest, Result<string>> Create()
        => new IdempotencyBehavior<ResultRequest, Result<string>>(
            new InMemoryIdempotencyStore(), new JsonIdempotencySerializer());

    [Fact]
    public async Task FailedResult_IsNotStored_SoRetryReExecutes()
    {
        var behavior = Create();
        var request = new ResultRequest { IdempotencyKey = "failed-" + Guid.NewGuid() };
        int calls = 0;

        await behavior.Handle(request,
            _ => { calls++; return Task.FromResult(Result<string>.Fail("transient")); },
            CancellationToken.None);
        var second = await behavior.Handle(request,
            _ => { calls++; return Task.FromResult(Result<string>.Ok("ok")); },
            CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.True(second.IsSuccess);
    }

    [Fact]
    public async Task SuccessfulResult_IsReplayedWithValue()
    {
        var behavior = Create();
        var request = new ResultRequest { IdempotencyKey = "ok-" + Guid.NewGuid() };
        int calls = 0;

        await behavior.Handle(request,
            _ => { calls++; return Task.FromResult(Result<string>.Ok("first")); },
            CancellationToken.None);
        var replay = await behavior.Handle(request,
            _ => { calls++; return Task.FromResult(Result<string>.Ok("second")); },
            CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.True(replay.IsSuccess);
        Assert.Equal("first", replay.Value);
    }
}
