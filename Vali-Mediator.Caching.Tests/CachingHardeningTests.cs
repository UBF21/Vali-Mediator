using Vali_Mediator.Core.Request;
using Vali_Mediator_Caching.Core.Abstractions;
using Vali_Mediator_Caching.Core.Enums;
using Vali_Mediator_Caching.Core.Interfaces;
using Vali_Mediator_Caching.Core.Store;
using Vali_Mediator_Caching.Pipeline;
using Xunit;

namespace Vali_Mediator_Caching.Tests;

file sealed class HardQuery : IRequest<string>, ICacheable
{
    public string CacheKey { get; init; } = "hard-key";
    public TimeSpan? AbsoluteExpiration { get; init; }
    public TimeSpan? SlidingExpiration { get; init; }
    public string? CacheGroup { get; init; }
    public bool BypassCache { get; init; }
    public CacheOrder Order { get; init; } = CacheOrder.ReadThenWrite;
}

// Store without IGroupAwareCacheStore (e.g. a backend that tags natively).
file sealed class PlainStore : ICacheStore
{
    private readonly InMemoryCacheStore _inner = new InMemoryCacheStore();

    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
        => _inner.TryGetAsync<T>(key, ct);

    public Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration, TimeSpan? slidingExpiration,
        CancellationToken ct = default)
        => _inner.SetAsync(key, value, absoluteExpiration, slidingExpiration, ct);

    public Task RemoveAsync(string key, CancellationToken ct = default) => _inner.RemoveAsync(key, ct);

    public Task RemoveByGroupAsync(string group, CancellationToken ct = default)
        => _inner.RemoveByGroupAsync(group, ct);
}

public sealed class CachingHardeningTests
{
    [Fact]
    public async Task ConcurrentMisses_SameKey_ExecuteHandlerOnce()
    {
        var store = new InMemoryCacheStore();
        var behavior = new CachingBehavior<HardQuery, string>(store);
        var calls = 0;
        var entered = new TaskCompletionSource();
        var gate = new TaskCompletionSource();

        Func<CancellationToken, Task<string>> next = async _ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await gate.Task;
            return "value";
        };

        var leader = Task.Run(() => behavior.Handle(new HardQuery { CacheKey = "once" }, next, CancellationToken.None));
        await entered.Task;
        var waiters = Enumerable.Range(0, 9)
            .Select(_ => Task.Run(() => behavior.Handle(new HardQuery { CacheKey = "once" }, next, CancellationToken.None)))
            .ToArray();
        Assert.True(SpinWait.SpinUntil(
            () => CachingBehavior<HardQuery, string>.WaitingCount == 9, TimeSpan.FromSeconds(10)));

        gate.SetResult();
        var results = await Task.WhenAll(waiters.Prepend(leader));

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal("value", r));
    }

    [Fact]
    public async Task ConcurrentMisses_AfterCompletion_LeaveNoKeyLocks()
    {
        var store = new InMemoryCacheStore();
        var behavior = new CachingBehavior<HardQuery, string>(store);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Task.Run(() => behavior.Handle(new HardQuery { CacheKey = "k" + (i % 3) },
                _ => Task.FromResult("v"), CancellationToken.None))));

        Assert.Equal(0, CachingBehavior<HardQuery, string>.PendingLockCount);
        Assert.Equal(0, CachingBehavior<HardQuery, string>.WaitingCount);
    }

    [Fact]
    public async Task HandlerThrows_ReleasesKeyLock_AndNextCallRuns()
    {
        var store = new InMemoryCacheStore();
        var behavior = new CachingBehavior<HardQuery, string>(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new HardQuery { CacheKey = "boom" },
                _ => throw new InvalidOperationException(), CancellationToken.None));

        var result = await behavior.Handle(new HardQuery { CacheKey = "boom" },
            _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(0, CachingBehavior<HardQuery, string>.PendingLockCount);
    }

    [Fact]
    public async Task NonGroupAwareStore_WithCacheGroup_StillCachesWithoutThrowing()
    {
        var behavior = new CachingBehavior<HardQuery, string>(new PlainStore());
        var calls = 0;
        Func<CancellationToken, Task<string>> next = _ => { calls++; return Task.FromResult("v"); };
        var request = new HardQuery { CacheKey = "g", CacheGroup = "grp" };

        await behavior.Handle(request, next, CancellationToken.None);
        await behavior.Handle(request, next, CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RemoveByGroup_ConcurrentWithRegister_NeverLeavesUntrackedKeys()
    {
        var store = new InMemoryCacheStore(new InMemoryCacheOptions { MaxEntries = 100_000 });
        var keys = Enumerable.Range(0, 2000).Select(i => "k" + i).ToArray();
        var done = false;

        var remover = Task.Run(async () =>
        {
            while (!Volatile.Read(ref done))
            {
                await store.RemoveByGroupAsync("g");
                await Task.Yield();
            }
        });

        await Task.WhenAll(keys.Select(k => Task.Run(async () =>
        {
            await store.SetAsync(k, "v", null, null);
            await store.RegisterKeyInGroupAsync("g", k);
        })));

        Volatile.Write(ref done, true);
        await remover;

        await store.RemoveByGroupAsync("g");

        foreach (var k in keys)
        {
            var (found, _) = await store.TryGetAsync<string>(k);
            Assert.False(found, $"{k} survived group removal");
        }
    }

    [Fact]
    public async Task KeyLongerThanStoreLimit_IsNotCached_ButHandlerStillRuns()
    {
        var store = new InMemoryCacheStore(new InMemoryCacheOptions { MaxKeyLength = 8 });
        var behavior = new CachingBehavior<HardQuery, string>(store);
        var calls = 0;
        Func<CancellationToken, Task<string>> next = _ => { calls++; return Task.FromResult("v"); };
        var request = new HardQuery { CacheKey = new string('k', 20) };

        await behavior.Handle(request, next, CancellationToken.None);
        await behavior.Handle(request, next, CancellationToken.None);

        Assert.Equal(2, calls);
    }
}
