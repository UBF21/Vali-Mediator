using Vali_Mediator_Idempotency.Core.Models;
using Vali_Mediator_Idempotency.Core.Store;
using Xunit;

namespace Vali_Mediator_Idempotency.Tests;

public class InMemoryIdempotencyStoreTests
{
    private static IdempotencyEntry MakeEntry(string key, DateTimeOffset? expiresAt = null)
        => new IdempotencyEntry
        {
            Key = key,
            SerializedResponse = new byte[] { 1, 2, 3 },
            ResponseTypeName = "System.String",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };

    // -------------------------------------------------------------------------
    // StoreAsync / FindAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoreAndFind_ReturnsStoredEntry()
    {
        var store = new InMemoryIdempotencyStore();
        var entry = MakeEntry("key-1");

        await store.StoreAsync(entry);
        var found = await store.FindAsync("key-1");

        Assert.NotNull(found);
        Assert.Equal("key-1", found.Key);
        Assert.Equal(new byte[] { 1, 2, 3 }, found.SerializedResponse);
    }

    [Fact]
    public async Task FindAsync_ReturnsNull_WhenKeyNotFound()
    {
        var store = new InMemoryIdempotencyStore();

        var found = await store.FindAsync("missing-key");

        Assert.Null(found);
    }

    // -------------------------------------------------------------------------
    // Expiry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindAsync_ReturnsNull_WhenEntryIsExpired()
    {
        var store = new InMemoryIdempotencyStore();
        var expiredEntry = MakeEntry("expired-key", DateTimeOffset.UtcNow.AddMilliseconds(-1));

        await store.StoreAsync(expiredEntry);
        var found = await store.FindAsync("expired-key");

        Assert.Null(found);
    }

    [Fact]
    public async Task FindAsync_ReturnsEntry_WhenNotYetExpired()
    {
        var store = new InMemoryIdempotencyStore();
        var entry = MakeEntry("live-key", DateTimeOffset.UtcNow.AddHours(1));

        await store.StoreAsync(entry);
        var found = await store.FindAsync("live-key");

        Assert.NotNull(found);
    }

    [Fact]
    public async Task FindAsync_ReturnsEntry_WhenNoExpiry()
    {
        var store = new InMemoryIdempotencyStore();
        var entry = MakeEntry("no-expiry-key");

        await store.StoreAsync(entry);
        var found = await store.FindAsync("no-expiry-key");

        Assert.NotNull(found);
    }

    // -------------------------------------------------------------------------
    // RemoveAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveAsync_DeletesEntry()
    {
        var store = new InMemoryIdempotencyStore();
        var entry = MakeEntry("remove-key");

        await store.StoreAsync(entry);
        await store.RemoveAsync("remove-key");
        var found = await store.FindAsync("remove-key");

        Assert.Null(found);
    }

    [Fact]
    public async Task RemoveAsync_IsNoOp_WhenKeyDoesNotExist()
    {
        var store = new InMemoryIdempotencyStore();

        var exception = await Record.ExceptionAsync(() => store.RemoveAsync("nonexistent"));

        Assert.Null(exception);
    }

    // -------------------------------------------------------------------------
    // ExistsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenLiveEntryExists()
    {
        var store = new InMemoryIdempotencyStore();
        var entry = MakeEntry("exists-key");

        await store.StoreAsync(entry);
        var exists = await store.ExistsAsync("exists-key");

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenKeyNotFound()
    {
        var store = new InMemoryIdempotencyStore();

        var exists = await store.ExistsAsync("ghost-key");

        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenEntryIsExpired()
    {
        var store = new InMemoryIdempotencyStore();
        var expiredEntry = MakeEntry("exp-key", DateTimeOffset.UtcNow.AddMilliseconds(-1));

        await store.StoreAsync(expiredEntry);
        var exists = await store.ExistsAsync("exp-key");

        Assert.False(exists);
    }
}
