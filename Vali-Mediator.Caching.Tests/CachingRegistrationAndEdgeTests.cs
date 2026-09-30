using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Caching.Core.Abstractions;
using Vali_Mediator_Caching.Core.Enums;
using Vali_Mediator_Caching.Core.Interfaces;
using Vali_Mediator_Caching.Core.Store;
using Vali_Mediator_Caching.Extension;
using Vali_Mediator_Caching.Pipeline;
using Xunit;

namespace Vali_Mediator_Caching.Tests;

file sealed class EdgeQuery : IRequest<string>, ICacheable
{
    public string CacheKey { get; init; } = "edge-key";
    public TimeSpan? AbsoluteExpiration => null;
    public TimeSpan? SlidingExpiration => null;
    public string? CacheGroup => null;
    public bool BypassCache => false;
    public CacheOrder Order => CacheOrder.ReadThenWrite;
}

// Reports a miss on the first lookup and a hit afterwards: another caller "populated" the entry in between.
file sealed class MissThenHitStore : ICacheStore
{
    private int _lookups;

    public int Lookups => _lookups;

    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
        => Task.FromResult(Interlocked.Increment(ref _lookups) == 1
            ? (false, default(T))
            : (true, (T?)(object)"from-another-caller"));

    public Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration, TimeSpan? slidingExpiration, CancellationToken ct = default)
        => throw new InvalidOperationException("The recheck hit must not write again.");

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveByGroupAsync(string group, CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class NoopStore : ICacheStore
{
    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
        => Task.FromResult((false, default(T)));

    public Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration, TimeSpan? slidingExpiration, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveByGroupAsync(string group, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Registration extensions, argument guards and the remaining store/behavior edge paths.</summary>
public sealed class CachingRegistrationAndEdgeTests
{
    [Fact]
    public void InMemoryCacheStore_NullOptions_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InMemoryCacheStore(null!));

    [Fact]
    public async Task TryGet_StoredValueOfAnotherType_IsAMiss()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("k", 42, null, null);

        var (found, value) = await store.TryGetAsync<string>("k");

        Assert.False(found);
        Assert.Null(value);
        Assert.True((await store.TryGetAsync<int>("k")).Found);
    }

    [Fact]
    public void CachingBehavior_NullStore_Throws()
        => Assert.Throws<ArgumentNullException>(() => new CachingBehavior<EdgeQuery, string>(null!));

    [Fact]
    public void CacheInvalidationBehavior_NullStore_Throws()
        => Assert.Throws<ArgumentNullException>(() => new CacheInvalidationBehavior<EdgeQuery, string>(null!));

    [Fact]
    public void AddInMemoryCacheStore_NullDelegate_Throws()
        => Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddInMemoryCacheStore(null!));

    [Fact]
    public void AddCachingOptions_NullDelegate_Throws()
        => Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddCachingOptions(null!));

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void AddCacheStore_RegistersTheCustomStoreWithTheRequestedLifetime(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();

        services.AddCacheStore<NoopStore>(lifetime);

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(ICacheStore), descriptor.ServiceType);
        Assert.Equal(typeof(NoopStore), descriptor.ImplementationType);
        Assert.Equal(lifetime, descriptor.Lifetime);
    }

    [Fact]
    public void AddCacheStore_DefaultsToSingleton_AndResolves()
    {
        var services = new ServiceCollection();
        services.AddCacheStore<NoopStore>();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<NoopStore>(provider.GetRequiredService<ICacheStore>());
        Assert.Same(provider.GetRequiredService<ICacheStore>(), provider.GetRequiredService<ICacheStore>());
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void AddCachingBehavior_RegistersCachingThenInvalidation_InThatOrder(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddInMemoryCacheStore();
        services.AddValiMediator(config => config.AddCachingBehavior(lifetime));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<EdgeQuery, string>>().ToList();

        Assert.Collection(
            behaviors,
            b => Assert.IsType<CachingBehavior<EdgeQuery, string>>(b),
            b => Assert.IsType<CacheInvalidationBehavior<EdgeQuery, string>>(b));
    }

    [Fact]
    public void AddCachingBehavior_DefaultLifetime_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddInMemoryCacheStore();
        services.AddValiMediator(config => config.AddCachingBehavior());
        using var provider = services.BuildServiceProvider();

        var first = provider.GetServices<IPipelineBehavior<EdgeQuery, string>>().First();
        var second = provider.GetServices<IPipelineBehavior<EdgeQuery, string>>().First();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task ReadThenWrite_WhenAnotherCallerPopulatedTheEntryFirst_ReturnsItWithoutRunningTheHandler()
    {
        var store = new MissThenHitStore();
        var behavior = new CachingBehavior<EdgeQuery, string>(store);
        var handlerRuns = 0;

        var result = await behavior.Handle(
            new EdgeQuery(),
            _ =>
            {
                Interlocked.Increment(ref handlerRuns);
                return Task.FromResult("handler-value");
            },
            CancellationToken.None);

        Assert.Equal("from-another-caller", result);
        Assert.Equal(0, handlerRuns);
        Assert.Equal(2, store.Lookups);
    }
}
