using Vali_Mediator.Core.Request;
using Vali_Mediator_Caching.Core.Interfaces;
using Vali_Mediator_Caching.Core.Store;
using Vali_Mediator_Caching.Pipeline;
using Xunit;

namespace Vali_Mediator_Caching.Tests;

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

file sealed class InvalidatingCommand : IRequest<Unit>, IInvalidatesCache
{
    public IReadOnlyList<string> InvalidatedKeys { get; init; } = new List<string>();
    public IReadOnlyList<string> InvalidatedGroups { get; init; } = new List<string>();
}

file sealed class NonInvalidatingCommand : IRequest<Unit>
{
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class CacheInvalidationBehaviorTests
{
    // -----------------------------------------------------------------------
    // Non-invalidating request — cache untouched
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NonInvalidatingRequest_DoesNotTouchCache()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("key1", "value", null, null);

        var behavior = new CacheInvalidationBehavior<NonInvalidatingCommand, Unit>(store);

        await behavior.Handle(
            new NonInvalidatingCommand(),
            () => Task.FromResult(Unit.Value),
            CancellationToken.None);

        var (found, _) = await store.TryGetAsync<string>("key1");
        Assert.True(found);
    }

    // -----------------------------------------------------------------------
    // Specific keys are evicted after handler executes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvalidatedKeys_AreRemovedAfterHandler()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("k1", "v1", null, null);
        await store.SetAsync("k2", "v2", null, null);
        await store.SetAsync("k3", "v3", null, null);

        var behavior = new CacheInvalidationBehavior<InvalidatingCommand, Unit>(store);
        var command = new InvalidatingCommand
        {
            InvalidatedKeys = new List<string> { "k1", "k2" },
            InvalidatedGroups = new List<string>(),
        };

        await behavior.Handle(command, () => Task.FromResult(Unit.Value), CancellationToken.None);

        var (f1, _) = await store.TryGetAsync<string>("k1");
        var (f2, _) = await store.TryGetAsync<string>("k2");
        var (f3, _) = await store.TryGetAsync<string>("k3");

        Assert.False(f1);
        Assert.False(f2);
        Assert.True(f3);
    }

    // -----------------------------------------------------------------------
    // Group invalidation evicts all keys in the group
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvalidatedGroups_EvictsAllKeysInGroup()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("a1", "v", null, null);
        await store.SetAsync("a2", "v", null, null);
        await store.SetAsync("b1", "v", null, null);

        await store.RegisterKeyInGroupAsync("groupA", "a1");
        await store.RegisterKeyInGroupAsync("groupA", "a2");
        // b1 is NOT in groupA

        var behavior = new CacheInvalidationBehavior<InvalidatingCommand, Unit>(store);
        var command = new InvalidatingCommand
        {
            InvalidatedKeys = new List<string>(),
            InvalidatedGroups = new List<string> { "groupA" },
        };

        await behavior.Handle(command, () => Task.FromResult(Unit.Value), CancellationToken.None);

        var (fa1, _) = await store.TryGetAsync<string>("a1");
        var (fa2, _) = await store.TryGetAsync<string>("a2");
        var (fb1, _) = await store.TryGetAsync<string>("b1");

        Assert.False(fa1);
        Assert.False(fa2);
        Assert.True(fb1);
    }

    // -----------------------------------------------------------------------
    // Both keys and groups invalidated simultaneously
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BothKeysAndGroups_AllEvicted()
    {
        var store = new InMemoryCacheStore();
        await store.SetAsync("direct-key", "v", null, null);
        await store.SetAsync("grp-key", "v", null, null);
        await store.RegisterKeyInGroupAsync("grpB", "grp-key");

        var behavior = new CacheInvalidationBehavior<InvalidatingCommand, Unit>(store);
        var command = new InvalidatingCommand
        {
            InvalidatedKeys = new List<string> { "direct-key" },
            InvalidatedGroups = new List<string> { "grpB" },
        };

        await behavior.Handle(command, () => Task.FromResult(Unit.Value), CancellationToken.None);

        var (f1, _) = await store.TryGetAsync<string>("direct-key");
        var (f2, _) = await store.TryGetAsync<string>("grp-key");

        Assert.False(f1);
        Assert.False(f2);
    }

    // -----------------------------------------------------------------------
    // Handler result is returned unchanged
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ReturnsHandlerResult()
    {
        var store = new InMemoryCacheStore();
        var behavior = new CacheInvalidationBehavior<InvalidatingCommand, Unit>(store);
        var command = new InvalidatingCommand
        {
            InvalidatedKeys = new List<string>(),
            InvalidatedGroups = new List<string>(),
        };

        var result = await behavior.Handle(
            command,
            () => Task.FromResult(Unit.Value),
            CancellationToken.None);

        Assert.Equal(Unit.Value, result);
    }
}
