using Vali_Mediator.Core.Request;
using Vali_Mediator_Caching.Core.Enums;
using Vali_Mediator_Caching.Core.Interfaces;
using Vali_Mediator_Caching.Core.Store;
using Vali_Mediator_Caching.Pipeline;
using Xunit;

namespace Vali_Mediator_Caching.Tests;

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

file sealed class CacheableQuery : IRequest<string>, ICacheable
{
    public string CacheKey { get; init; } = "test-key";
    public TimeSpan? AbsoluteExpiration { get; init; }
    public TimeSpan? SlidingExpiration { get; init; }
    public string? CacheGroup { get; init; }
    public bool BypassCache { get; init; }
    public CacheOrder Order { get; init; } = CacheOrder.ReadThenWrite;
}

file sealed class PlainQuery : IRequest<string>
{
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class CachingBehaviorTests
{
    private static CachingBehavior<TRequest, TResponse> CreateBehavior<TRequest, TResponse>(
        InMemoryCacheStore store)
        where TRequest : IRequest<TResponse>
        => new CachingBehavior<TRequest, TResponse>(store);

    private static Func<Task<string>> HandlerReturning(string value, ref int callCount)
    {
        int localCount = 0;
        callCount = 0;
        Func<Task<string>> fn = () =>
        {
            localCount++;
            return Task.FromResult(value);
        };
        // capture ref through a closure wrapper
        return fn;
    }

    // -----------------------------------------------------------------------
    // Non-cacheable request — passes through untouched
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NonCacheableRequest_PassesThroughDirectly()
    {
        var store = new InMemoryCacheStore();
        var behavior = new CachingBehavior<PlainQuery, string>(store);

        int calls = 0;
        var result = await behavior.Handle(
            new PlainQuery(),
            () => { calls++; return Task.FromResult("plain"); },
            CancellationToken.None);

        Assert.Equal("plain", result);
        Assert.Equal(1, calls);
    }

    // -----------------------------------------------------------------------
    // Cache miss → handler executes → result cached
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FirstCall_CacheMiss_ExecutesHandlerAndCachesResult()
    {
        var store = new InMemoryCacheStore();
        var behavior = new CachingBehavior<CacheableQuery, string>(store);
        var request = new CacheableQuery { CacheKey = "q1" };

        int calls = 0;
        var result = await behavior.Handle(
            request,
            () => { calls++; return Task.FromResult("value1"); },
            CancellationToken.None);

        Assert.Equal("value1", result);
        Assert.Equal(1, calls);

        // Verify it was stored
        var (found, cached) = await store.TryGetAsync<string>("q1");
        Assert.True(found);
        Assert.Equal("value1", cached);
    }

    // -----------------------------------------------------------------------
    // Cache hit on second call — handler NOT invoked
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SecondCall_CacheHit_ReturnsFromCacheWithoutCallingHandler()
    {
        var store = new InMemoryCacheStore();
        var behavior = new CachingBehavior<CacheableQuery, string>(store);
        var request = new CacheableQuery { CacheKey = "q1" };

        int calls = 0;
        Func<Task<string>> handler = () => { calls++; return Task.FromResult("fresh"); };

        await behavior.Handle(request, handler, CancellationToken.None);
        var second = await behavior.Handle(request, handler, CancellationToken.None);

        Assert.Equal("fresh", second);
        Assert.Equal(1, calls); // handler only called once
    }

    // -----------------------------------------------------------------------
    // BypassCache — always hits handler, then refreshes cache
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BypassCache_AlwaysExecutesHandler()
    {
        var store = new InMemoryCacheStore();
        var behavior = new CachingBehavior<CacheableQuery, string>(store);
        var request = new CacheableQuery { CacheKey = "q2", BypassCache = true };

        int calls = 0;
        Func<Task<string>> handler = () => { calls++; return Task.FromResult("bypassed"); };

        await behavior.Handle(request, handler, CancellationToken.None);
        await behavior.Handle(request, handler, CancellationToken.None);

        Assert.Equal(2, calls);
    }

    // -----------------------------------------------------------------------
    // WriteOnly — skips read, always writes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WriteOnly_SkipsCacheRead_AlwaysCallsHandler()
    {
        var store = new InMemoryCacheStore();
        // Pre-populate the cache
        await store.SetAsync("q3", "stale", null, null);

        var behavior = new CachingBehavior<CacheableQuery, string>(store);
        var request = new CacheableQuery { CacheKey = "q3", Order = CacheOrder.WriteOnly };

        int calls = 0;
        var result = await behavior.Handle(
            request,
            () => { calls++; return Task.FromResult("fresh"); },
            CancellationToken.None);

        Assert.Equal("fresh", result);
        Assert.Equal(1, calls);

        // Cache should now hold the fresh value
        var (found, cached) = await store.TryGetAsync<string>("q3");
        Assert.True(found);
        Assert.Equal("fresh", cached);
    }

    // -----------------------------------------------------------------------
    // ReadOnly — reads cache but does NOT write after handler
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReadOnly_OnCacheMiss_DoesNotWriteResult()
    {
        var store = new InMemoryCacheStore();
        var behavior = new CachingBehavior<CacheableQuery, string>(store);
        var request = new CacheableQuery { CacheKey = "q4", Order = CacheOrder.ReadOnly };

        await behavior.Handle(
            request,
            () => Task.FromResult("data"),
            CancellationToken.None);

        var (found, _) = await store.TryGetAsync<string>("q4");
        Assert.False(found); // result was NOT cached
    }

    [Fact]
    public async Task ReadOnly_OnCacheHit_ReturnsCachedValue()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("q5", "cached", null, null);

        var behavior = new CachingBehavior<CacheableQuery, string>(store);
        var request = new CacheableQuery { CacheKey = "q5", Order = CacheOrder.ReadOnly };

        int calls = 0;
        var result = await behavior.Handle(
            request,
            () => { calls++; return Task.FromResult("fresh"); },
            CancellationToken.None);

        Assert.Equal("cached", result);
        Assert.Equal(0, calls);
    }

    // -----------------------------------------------------------------------
    // CacheGroup — key is registered in group
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CacheGroup_KeyRegisteredAndGroupInvalidationEvictsIt()
    {
        var store = new InMemoryCacheStore();
        var behavior = new CachingBehavior<CacheableQuery, string>(store);
        var request = new CacheableQuery { CacheKey = "q6", CacheGroup = "grp1" };

        await behavior.Handle(request, () => Task.FromResult("grouped"), CancellationToken.None);

        // Verify entry is present
        var (f1, _) = await store.TryGetAsync<string>("q6");
        Assert.True(f1);

        // Invalidate by group
        await store.RemoveByGroupAsync("grp1");

        var (f2, _) = await store.TryGetAsync<string>("q6");
        Assert.False(f2);
    }
}
