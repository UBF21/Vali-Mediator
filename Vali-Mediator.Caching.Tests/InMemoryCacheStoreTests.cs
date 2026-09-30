using Vali_Mediator_Caching.Core.Store;
using Xunit;

namespace Vali_Mediator_Caching.Tests;

public sealed class InMemoryCacheStoreTests
{
    private readonly FakeTimeProvider _clock = new FakeTimeProvider();

    private InMemoryCacheStore NewStore(Action<InMemoryCacheOptions>? configure = null)
    {
        var options = new InMemoryCacheOptions { TimeProvider = _clock };
        configure?.Invoke(options);
        return new InMemoryCacheStore(options);
    }

    // -------------------------------------------------------------------------
    // Set / Get
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetAsync_MissingKey_ReturnsFalse()
    {
        var store = NewStore();

        var (found, value) = await store.TryGetAsync<string>("missing-key");

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public async Task TryGetAsync_ExistingKey_ReturnsTrueAndValue()
    {
        var store = NewStore();
        await store.SetAsync("key1", "hello", null, null);

        var (found, value) = await store.TryGetAsync<string>("key1");

        Assert.True(found);
        Assert.Equal("hello", value);
    }

    [Fact]
    public async Task TryGetAsync_AfterRemove_ReturnsFalse()
    {
        var store = NewStore();
        await store.SetAsync("key1", 42, null, null);
        await store.RemoveAsync("key1");

        var (found, _) = await store.TryGetAsync<int>("key1");

        Assert.False(found);
    }

    [Fact]
    public async Task DefaultConstructor_Works()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("k", "v", null, null);

        Assert.True((await store.TryGetAsync<string>("k")).Found);
    }

    // -------------------------------------------------------------------------
    // Absolute expiry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetAsync_AbsoluteExpiryNotReached_ReturnsValue()
    {
        var store = NewStore();
        await store.SetAsync("key1", "data", TimeSpan.FromMinutes(5), null);
        _clock.Advance(TimeSpan.FromMinutes(4));

        var (found, value) = await store.TryGetAsync<string>("key1");

        Assert.True(found);
        Assert.Equal("data", value);
    }

    [Fact]
    public async Task TryGetAsync_AbsoluteExpiryElapsed_ReturnsFalse()
    {
        var store = NewStore();
        await store.SetAsync("key1", "data", TimeSpan.FromMinutes(5), null);
        _clock.Advance(TimeSpan.FromMinutes(5));

        var (found, _) = await store.TryGetAsync<string>("key1");

        Assert.False(found);
    }

    // -------------------------------------------------------------------------
    // Sliding expiry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetAsync_SlidingExpiry_ResetsOnAccess()
    {
        var store = NewStore();
        await store.SetAsync("key1", "data", null, TimeSpan.FromSeconds(10));

        _clock.Advance(TimeSpan.FromSeconds(8));
        Assert.True((await store.TryGetAsync<string>("key1")).Found);

        // 16s since the write but only 8s since the last read: still alive.
        _clock.Advance(TimeSpan.FromSeconds(8));
        Assert.True((await store.TryGetAsync<string>("key1")).Found);
    }

    [Fact]
    public async Task TryGetAsync_SlidingExpiry_ExpiresAfterInactivity()
    {
        var store = NewStore();
        await store.SetAsync("key1", "data", null, TimeSpan.FromSeconds(10));

        _clock.Advance(TimeSpan.FromSeconds(10));

        Assert.False((await store.TryGetAsync<string>("key1")).Found);
    }

    // -------------------------------------------------------------------------
    // RemoveByGroup
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveByGroupAsync_RemovesAllKeysInGroup()
    {
        var store = NewStore();
        await store.SetAsync("k1", "v1", null, null);
        await store.SetAsync("k2", "v2", null, null);
        await store.SetAsync("k3", "v3", null, null);

        await store.RegisterKeyInGroupAsync("grp", "k1");
        await store.RegisterKeyInGroupAsync("grp", "k2");

        await store.RemoveByGroupAsync("grp");

        Assert.False((await store.TryGetAsync<string>("k1")).Found);
        Assert.False((await store.TryGetAsync<string>("k2")).Found);
        Assert.True((await store.TryGetAsync<string>("k3")).Found);
    }

    [Fact]
    public async Task RemoveByGroupAsync_NonExistentGroup_DoesNotThrow()
    {
        var store = NewStore();
        var ex = await Record.ExceptionAsync(() => store.RemoveByGroupAsync("ghost-group"));
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // MaxEntries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_RespectsMaxEntries_ByEvictingInsteadOfDropping()
    {
        var store = NewStore(o => o.MaxEntries = 2);
        await store.SetAsync("k1", "v1", null, null);
        await store.SetAsync("k2", "v2", null, null);
        await store.SetAsync("k3", "v3", null, null);

        Assert.Equal(2, store.Count);
        Assert.False((await store.TryGetAsync<string>("k1")).Found);
        Assert.True((await store.TryGetAsync<string>("k3")).Found);
    }

    [Fact]
    public async Task SetAsync_UpdateExistingKey_AllowedEvenAtCapacity()
    {
        var store = NewStore(o => o.MaxEntries = 1);
        await store.SetAsync("k1", "original", null, null);
        await store.SetAsync("k1", "updated", null, null);

        var (found, value) = await store.TryGetAsync<string>("k1");
        Assert.True(found);
        Assert.Equal("updated", value);
    }
}
