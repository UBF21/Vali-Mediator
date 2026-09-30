using Vali_Mediator_Caching.Core.Store;
using Xunit;

namespace Vali_Mediator_Caching.Tests;

public sealed class CacheStoreLimitsTests
{
    private readonly FakeTimeProvider _clock = new FakeTimeProvider();

    private InMemoryCacheStore NewStore(Action<InMemoryCacheOptions>? configure = null)
    {
        var options = new InMemoryCacheOptions { TimeProvider = _clock };
        configure?.Invoke(options);
        return new InMemoryCacheStore(options);
    }

    // -------------------------------------------------------------------------
    // Option validation
    // -------------------------------------------------------------------------

    [Fact]
    public void Options_HaveSaneDefaults()
    {
        var o = new InMemoryCacheOptions();

        Assert.Equal(10_000, o.MaxEntries);
        Assert.Equal(512, o.MaxKeyLength);
        Assert.Equal(10_000, o.MaxGroups);
        Assert.Equal(10_000, o.MaxKeysPerGroup);
        Assert.Equal(TimeSpan.FromMinutes(5), o.CleanupInterval);
        Assert.Same(TimeProvider.System, o.TimeProvider);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Options_RejectNonPositiveLimits(int value)
    {
        var o = new InMemoryCacheOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => o.MaxEntries = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => o.MaxKeyLength = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => o.MaxGroups = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => o.MaxKeysPerGroup = value);
    }

    [Fact]
    public void Options_RejectNonPositiveCleanupInterval_AndNullTimeProvider()
    {
        var o = new InMemoryCacheOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => o.CleanupInterval = TimeSpan.Zero);
        Assert.Throws<ArgumentNullException>(() => o.TimeProvider = null!);
    }

    // -------------------------------------------------------------------------
    // MaxKeyLength
    // -------------------------------------------------------------------------

    [Fact]
    public async Task KeyLongerThanMaxKeyLength_IsNeverStored()
    {
        var store = NewStore(o => o.MaxKeyLength = 8);
        var longKey = new string('x', 9);

        await store.SetAsync(longKey, "v", null, null);

        Assert.Equal(0, store.Count);
        Assert.False((await store.TryGetAsync<string>(longKey)).Found);
    }

    [Fact]
    public async Task KeyAtMaxKeyLength_IsStored()
    {
        var store = NewStore(o => o.MaxKeyLength = 8);
        var key = new string('x', 8);

        await store.SetAsync(key, "v", null, null);

        Assert.True((await store.TryGetAsync<string>(key)).Found);
    }

    // -------------------------------------------------------------------------
    // LRU eviction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenFull_EvictsLeastRecentlyAccessed()
    {
        var store = NewStore(o => o.MaxEntries = 2);
        await store.SetAsync("old", "v", null, null);
        await store.SetAsync("recent", "v", null, null);
        await store.TryGetAsync<string>("old");
        // "recent" is now the least recently used.

        await store.SetAsync("new", "v", null, null);

        Assert.False((await store.TryGetAsync<string>("recent")).Found);
        Assert.True((await store.TryGetAsync<string>("old")).Found);
        Assert.True((await store.TryGetAsync<string>("new")).Found);
    }

    [Fact]
    public async Task ExpiredEntries_AreSweptOnceCleanupIntervalElapses()
    {
        var store = NewStore(o => o.CleanupInterval = TimeSpan.FromMinutes(1));
        await store.SetAsync("a", "v", TimeSpan.FromSeconds(10), null);
        await store.SetAsync("b", "v", TimeSpan.FromSeconds(10), null);
        _clock.Advance(TimeSpan.FromMinutes(2));

        await store.SetAsync("c", "v", null, null);

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task ExpiredKey_IsRemovedFromItsGroupIndex()
    {
        var store = NewStore();
        await store.SetAsync("k", "v", TimeSpan.FromSeconds(30), null);
        await store.RegisterKeyInGroupAsync("g", "k");
        Assert.Equal(1, store.GroupCount);

        _clock.Advance(TimeSpan.FromSeconds(30));
        await store.TryGetAsync<string>("k");

        Assert.Equal(0, store.GroupCount);
    }

    [Fact]
    public async Task RemoveAsync_RemovesKeyFromItsGroupIndex()
    {
        var store = NewStore();
        await store.SetAsync("k", "v", null, null);
        await store.RegisterKeyInGroupAsync("g", "k");

        await store.RemoveAsync("k");

        Assert.Equal(0, store.GroupCount);
    }

    [Fact]
    public async Task EvictedEntry_LeavesNoGroupBehind()
    {
        var store = NewStore(o => o.MaxEntries = 1);
        await store.SetAsync("a", "v", null, null);
        await store.RegisterKeyInGroupAsync("g", "a");

        await store.SetAsync("b", "v", null, null);

        Assert.Equal(0, store.GroupCount);
    }

    // -------------------------------------------------------------------------
    // Groups (AUD-C-04, AUD-S-05, AUD-C-10)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegisterKeyInGroup_ForMissingKey_DoesNotCreateOrphan()
    {
        var store = NewStore();

        await store.RegisterKeyInGroupAsync("g", "never-stored");

        Assert.Equal(0, store.GroupCount);
        Assert.Equal(0, store.GroupedKeyCount);
    }

    [Fact]
    public async Task RecreatedKey_AfterExpiry_IsStillInvalidatedByItsGroup()
    {
        var store = NewStore();
        await store.SetAsync("k", "old", TimeSpan.FromSeconds(5), null);
        await store.RegisterKeyInGroupAsync("g", "k");
        _clock.Advance(TimeSpan.FromSeconds(5));
        await store.TryGetAsync<string>("k"); // lazy eviction of the expired entry

        await store.SetAsync("k", "new", null, null);
        await store.RegisterKeyInGroupAsync("g", "k");
        await store.RemoveByGroupAsync("g");

        Assert.False((await store.TryGetAsync<string>("k")).Found);
    }

    [Fact]
    public async Task ReRegisteringKeyUnderAnotherGroup_MovesIt()
    {
        var store = NewStore();
        await store.SetAsync("k", "v", null, null);
        await store.RegisterKeyInGroupAsync("g1", "k");
        await store.RegisterKeyInGroupAsync("g2", "k");

        await store.RemoveByGroupAsync("g1");
        Assert.True((await store.TryGetAsync<string>("k")).Found);

        await store.RemoveByGroupAsync("g2");
        Assert.False((await store.TryGetAsync<string>("k")).Found);
        Assert.Equal(0, store.GroupCount);
    }

    [Fact]
    public async Task ExceedingMaxKeysPerGroup_DropsTheEntryInsteadOfLeavingItUninvalidatable()
    {
        var store = NewStore(o => o.MaxKeysPerGroup = 2);
        for (var i = 0; i < 3; i++)
        {
            await store.SetAsync("k" + i, "v", null, null);
            await store.RegisterKeyInGroupAsync("g", "k" + i);
        }

        Assert.Equal(2, store.GroupedKeyCount);
        Assert.False((await store.TryGetAsync<string>("k2")).Found);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public async Task ExceedingMaxGroups_DropsTheEntry()
    {
        var store = NewStore(o => o.MaxGroups = 1);
        await store.SetAsync("a", "v", null, null);
        await store.RegisterKeyInGroupAsync("g1", "a");
        await store.SetAsync("b", "v", null, null);

        await store.RegisterKeyInGroupAsync("g2", "b");

        Assert.Equal(1, store.GroupCount);
        Assert.False((await store.TryGetAsync<string>("b")).Found);
        Assert.True((await store.TryGetAsync<string>("a")).Found);
    }

    [Fact]
    public async Task GroupNameLongerThanMaxKeyLength_DropsTheEntry()
    {
        var store = NewStore(o => o.MaxKeyLength = 4);
        await store.SetAsync("k", "v", null, null);

        await store.RegisterKeyInGroupAsync("too-long-group", "k");

        Assert.Equal(0, store.GroupCount);
        Assert.False((await store.TryGetAsync<string>("k")).Found);
    }

    [Fact]
    public async Task ConcurrentSetRegisterRemove_NeverLeavesOrphansNorExceedsMaxEntries()
    {
        const int maxEntries = 50;
        var store = NewStore(o => o.MaxEntries = maxEntries);
        var keys = Enumerable.Range(0, 200).Select(i => "k" + i).ToArray();

        var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(async () =>
        {
            var rnd = new Random(worker);
            for (var i = 0; i < 500; i++)
            {
                var key = keys[rnd.Next(keys.Length)];
                switch (rnd.Next(4))
                {
                    case 0:
                        await store.RemoveAsync(key);
                        break;
                    case 1:
                        await store.RemoveByGroupAsync("g");
                        break;
                    default:
                        await store.SetAsync(key, "v", null, null);
                        await store.RegisterKeyInGroupAsync("g", key);
                        break;
                }

                Assert.True(store.Count <= maxEntries);
            }
        })).ToArray();
        await Task.WhenAll(workers);

        Assert.True(store.Count <= maxEntries);
        Assert.Equal(store.Count, store.GroupedKeyCount);

        await store.RemoveByGroupAsync("g");
        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.GroupCount);
    }
}
