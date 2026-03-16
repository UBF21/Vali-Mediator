using System.Collections.Concurrent;
using Vali_Mediator_Idempotency.Core.Abstractions;
using Vali_Mediator_Idempotency.Core.Models;

namespace Vali_Mediator_Idempotency.Core.Store;

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="IIdempotencyStore"/> backed by
/// a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
/// <remarks>
/// Expired entries are auto-evicted on <see cref="FindAsync"/>.
/// A lazy sweep removes all expired entries every 100 write operations.
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private const int CleanupThreshold = 100;

    private readonly ConcurrentDictionary<string, IdempotencyEntry> _store =
        new ConcurrentDictionary<string, IdempotencyEntry>(StringComparer.Ordinal);

    private int _writeCounter;

    /// <inheritdoc />
    public Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(key, out var entry))
            return Task.FromResult<IdempotencyEntry?>(null);

        if (entry.IsExpired)
        {
            _store.TryRemove(key, out _);
            return Task.FromResult<IdempotencyEntry?>(null);
        }

        return Task.FromResult<IdempotencyEntry?>(entry);
    }

    /// <inheritdoc />
    public Task StoreAsync(IdempotencyEntry entry, CancellationToken ct = default)
    {
        _store[entry.Key] = entry;

        var counter = System.Threading.Interlocked.Increment(ref _writeCounter);
        if (counter % CleanupThreshold == 0)
            SweepExpired();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(key, out var entry))
            return Task.FromResult(false);

        if (entry.IsExpired)
        {
            _store.TryRemove(key, out _);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private void SweepExpired()
    {
        foreach (var kvp in _store)
        {
            if (kvp.Value.IsExpired)
                _store.TryRemove(kvp.Key, out _);
        }
    }
}
