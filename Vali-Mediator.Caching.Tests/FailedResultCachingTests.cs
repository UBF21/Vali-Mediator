using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Vali_Mediator_Caching.Core.Enums;
using Vali_Mediator_Caching.Core.Interfaces;
using Vali_Mediator_Caching.Core.Store;
using Vali_Mediator_Caching.Pipeline;
using Xunit;

namespace Vali_Mediator_Caching.Tests;

file sealed class ResultQuery : IRequest<Result<string>>, ICacheable
{
    public string CacheKey { get; init; } = "result-key";
    public TimeSpan? AbsoluteExpiration { get; init; }
    public TimeSpan? SlidingExpiration { get; init; }
    public string? CacheGroup { get; init; }
    public bool BypassCache { get; init; }
    public CacheOrder Order { get; init; } = CacheOrder.ReadThenWrite;
}

file sealed class ResultCommand : IRequest<Result<string>>, IInvalidatesCache
{
    public IReadOnlyList<string> InvalidatedKeys { get; init; } = new List<string> { "k1" };
    public IReadOnlyList<string> InvalidatedGroups { get; init; } = new List<string>();
}

public sealed class FailedResultCachingTests
{
    [Fact]
    public async Task FailedResult_IsNotCached()
    {
        var behavior = new CachingBehavior<ResultQuery, Result<string>>(new InMemoryCacheStore());
        int calls = 0;

        await behavior.Handle(new ResultQuery(),
            _ => { calls++; return Task.FromResult(Result<string>.Fail("nope", ErrorType.NotFound)); },
            CancellationToken.None);
        await behavior.Handle(new ResultQuery(),
            _ => { calls++; return Task.FromResult(Result<string>.Fail("nope", ErrorType.NotFound)); },
            CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SuccessfulResult_IsCached()
    {
        var behavior = new CachingBehavior<ResultQuery, Result<string>>(new InMemoryCacheStore());
        int calls = 0;

        await behavior.Handle(new ResultQuery(),
            _ => { calls++; return Task.FromResult(Result<string>.Ok("v")); },
            CancellationToken.None);
        await behavior.Handle(new ResultQuery(),
            _ => { calls++; return Task.FromResult(Result<string>.Ok("v")); },
            CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailedResult_DoesNotInvalidateCache()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("k1", "v1", null, null);
        var behavior = new CacheInvalidationBehavior<ResultCommand, Result<string>>(store);

        await behavior.Handle(new ResultCommand(),
            _ => Task.FromResult(Result<string>.Fail("boom")),
            CancellationToken.None);

        var (found, _) = await store.TryGetAsync<string>("k1");
        Assert.True(found);
    }

    [Fact]
    public async Task SuccessfulResult_InvalidatesCache()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("k1", "v1", null, null);
        var behavior = new CacheInvalidationBehavior<ResultCommand, Result<string>>(store);

        await behavior.Handle(new ResultCommand(),
            _ => Task.FromResult(Result<string>.Ok("done")),
            CancellationToken.None);

        var (found, _) = await store.TryGetAsync<string>("k1");
        Assert.False(found);
    }
}
