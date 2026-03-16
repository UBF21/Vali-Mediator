using Vali_Mediator_Caching.Core.Store;
using Xunit;

namespace Vali_Mediator_Caching.Tests;

public sealed class InMemoryCacheStoreTests
{
    // -------------------------------------------------------------------------
    // Set / Get
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetAsync_MissingKey_ReturnsFalse()
    {
        var store = new InMemoryCacheStore();

        var (found, value) = await store.TryGetAsync<string>("missing-key");

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public async Task TryGetAsync_ExistingKey_ReturnsTrueAndValue()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("key1", "hello", null, null);

        var (found, value) = await store.TryGetAsync<string>("key1");

        Assert.True(found);
        Assert.Equal("hello", value);
    }

    [Fact]
    public async Task TryGetAsync_AfterRemove_ReturnsFalse()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("key1", 42, null, null);
        await store.RemoveAsync("key1");

        var (found, _) = await store.TryGetAsync<int>("key1");

        Assert.False(found);
    }

    // -------------------------------------------------------------------------
    // Absolute expiry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetAsync_AbsoluteExpiryNotReached_ReturnsValue()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("key1", "data", TimeSpan.FromMinutes(5), null);

        var (found, value) = await store.TryGetAsync<string>("key1");

        Assert.True(found);
        Assert.Equal("data", value);
    }

    [Fact]
    public async Task TryGetAsync_AbsoluteExpiryElapsed_ReturnsFalse()
    {
        // Use a very small expiry and simulate by setting absolute to the past
        // We bypass the timer by using negative TimeSpan trick — instead use
        // a store backed by a manipulatable clock. Since InMemoryCacheStore uses
        // DateTimeOffset.UtcNow internally, we test with a tiny sleep.
        var store = new InMemoryCacheStore();
        await store.SetAsync("key1", "data", TimeSpan.FromMilliseconds(10), null);

        await Task.Delay(50); // let the entry expire

        var (found, _) = await store.TryGetAsync<string>("key1");

        Assert.False(found);
    }

    // -------------------------------------------------------------------------
    // Sliding expiry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetAsync_SlidingExpiry_ResetsOnAccess()
    {
        var store = new InMemoryCacheStore();
        // Sliding window of 100ms
        await store.SetAsync("key1", "data", null, TimeSpan.FromMilliseconds(100));

        // Access at ~40ms — should still be alive
        await Task.Delay(40);
        var (found1, _) = await store.TryGetAsync<string>("key1");
        Assert.True(found1);

        // Access again at ~40ms after the last read — still within window
        await Task.Delay(40);
        var (found2, _) = await store.TryGetAsync<string>("key1");
        Assert.True(found2);
    }

    [Fact]
    public async Task TryGetAsync_SlidingExpiry_ExpiresAfterInactivity()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("key1", "data", null, TimeSpan.FromMilliseconds(30));

        await Task.Delay(80); // no access — should expire

        var (found, _) = await store.TryGetAsync<string>("key1");
        Assert.False(found);
    }

    // -------------------------------------------------------------------------
    // RemoveByGroup
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveByGroupAsync_RemovesAllKeysInGroup()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("k1", "v1", null, null);
        await store.SetAsync("k2", "v2", null, null);
        await store.SetAsync("k3", "v3", null, null);

        await store.RegisterKeyInGroupAsync("grp", "k1");
        await store.RegisterKeyInGroupAsync("grp", "k2");

        await store.RemoveByGroupAsync("grp");

        var (f1, _) = await store.TryGetAsync<string>("k1");
        var (f2, _) = await store.TryGetAsync<string>("k2");
        var (f3, _) = await store.TryGetAsync<string>("k3");

        Assert.False(f1);
        Assert.False(f2);
        Assert.True(f3); // not in the group — still present
    }

    [Fact]
    public async Task RemoveByGroupAsync_NonExistentGroup_DoesNotThrow()
    {
        var store = new InMemoryCacheStore();
        var ex = await Record.ExceptionAsync(() => store.RemoveByGroupAsync("ghost-group"));
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // MaxEntries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_RespectsMaxEntries()
    {
        var store = new InMemoryCacheStore(new InMemoryCacheOptions { MaxEntries = 2 });
        await store.SetAsync("k1", "v1", null, null);
        await store.SetAsync("k2", "v2", null, null);
        await store.SetAsync("k3", "v3", null, null); // should be silently ignored

        var (f3, _) = await store.TryGetAsync<string>("k3");
        Assert.False(f3);
    }

    [Fact]
    public async Task SetAsync_UpdateExistingKey_AllowedEvenAtCapacity()
    {
        var store = new InMemoryCacheStore(new InMemoryCacheOptions { MaxEntries = 1 });
        await store.SetAsync("k1", "original", null, null);
        await store.SetAsync("k1", "updated", null, null); // update, not new entry

        var (found, value) = await store.TryGetAsync<string>("k1");
        Assert.True(found);
        Assert.Equal("updated", value);
    }
}
