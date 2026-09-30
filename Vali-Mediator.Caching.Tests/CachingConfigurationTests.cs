using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator_Caching.Core.Abstractions;
using Vali_Mediator_Caching.Core.Store;
using Vali_Mediator_Caching.Extension;
using Vali_Mediator_Caching.Pipeline;
using Xunit;

namespace Vali_Mediator_Caching.Tests;

/// <summary>Cache limits configured through the registration extensions, across value matrices.</summary>
public class CachingConfigurationTests
{
    private static ServiceProvider Build(Action<InMemoryCacheOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddInMemoryCacheStore(configure);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(25)]
    public async Task MaxEntries_ConfiguredThroughDi_BoundsTheStore(int maxEntries)
    {
        await using var provider = Build(o => o.MaxEntries = maxEntries);
        var store = (InMemoryCacheStore)provider.GetRequiredService<ICacheStore>();

        for (var i = 0; i < maxEntries + 10; i++)
            await store.SetAsync("k" + i, i, null, null);

        Assert.Equal(maxEntries, store.Count);
        Assert.True((await store.TryGetAsync<int>("k" + (maxEntries + 9))).Found);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(16, 16, true)]
    [InlineData(16, 17, false)]
    public async Task MaxKeyLength_ConfiguredThroughDi_CutsLongKeys(int maxKeyLength, int keyLength, bool stored)
    {
        await using var provider = Build(o => o.MaxKeyLength = maxKeyLength);
        var store = (InMemoryCacheStore)provider.GetRequiredService<ICacheStore>();
        var key = new string('k', keyLength);

        await store.SetAsync(key, "v", null, null);

        Assert.Equal(stored, (await store.TryGetAsync<string>(key)).Found);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void StoreLimits_RejectNonPositive_AtConfigurationTime(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(o => o.MaxEntries = value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(o => o.MaxKeyLength = value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(o => o.MaxGroups = value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(o => o.MaxKeysPerGroup = value));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void StoreLimits_AcceptTheExtremesOfTheValidRange(int value)
    {
        using var provider = Build(o =>
        {
            o.MaxEntries = value;
            o.MaxKeyLength = value;
            o.MaxGroups = value;
            o.MaxKeysPerGroup = value;
        });

        Assert.NotNull(provider.GetRequiredService<ICacheStore>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CleanupInterval_RejectsZeroAndNegative(int seconds)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new InMemoryCacheOptions { CleanupInterval = TimeSpan.FromSeconds(seconds) });

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CoalescingWaitTimeout_RejectsZeroAndNegative(int seconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CachingOptions { CoalescingWaitTimeout = TimeSpan.FromSeconds(seconds) });
        Assert.Equal("CoalescingWaitTimeout", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ServiceCollection().AddCachingOptions(o => o.CoalescingWaitTimeout = TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(3600)]
    public void CoalescingWaitTimeout_AcceptsPositive_AndIsRegisteredThroughDi(int seconds)
    {
        var services = new ServiceCollection();
        services.AddCachingOptions(o => o.CoalescingWaitTimeout = TimeSpan.FromSeconds(seconds));
        using var provider = services.BuildServiceProvider();

        Assert.Equal(TimeSpan.FromSeconds(seconds), provider.GetRequiredService<CachingOptions>().CoalescingWaitTimeout);
    }

    [Fact]
    public void CoalescingWaitTimeout_AcceptsInfinite()
        => Assert.Equal(Timeout.InfiniteTimeSpan, new CachingOptions { CoalescingWaitTimeout = Timeout.InfiniteTimeSpan }.CoalescingWaitTimeout);

    [Fact]
    public void Defaults_AreUnchanged()
    {
        var store = new InMemoryCacheOptions();
        Assert.Equal(10_000, store.MaxEntries);
        Assert.Equal(512, store.MaxKeyLength);
        Assert.Equal(10_000, store.MaxGroups);
        Assert.Equal(10_000, store.MaxKeysPerGroup);
        Assert.Equal(TimeSpan.FromMinutes(5), store.CleanupInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), new CachingOptions().CoalescingWaitTimeout);
    }
}
