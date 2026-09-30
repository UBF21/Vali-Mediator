using Vali_Mediator_Idempotency.Core.Abstractions;
using Vali_Mediator_Idempotency.Core.Models;
using Vali_Mediator_Idempotency.Core.Options;

namespace Vali_Mediator_Idempotency.Core.Store;

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="IIdempotencyStore"/> with bounded capacity.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Capacity is capped by <see cref="InMemoryIdempotencyStoreOptions.MaxEntries"/>; the oldest entries are evicted first.</item>
///   <item>Entries stored without an expiry receive <see cref="InMemoryIdempotencyStoreOptions.DefaultExpiration"/>.</item>
///   <item>Expired entries are evicted on lookup, and swept in bulk every few writes.</item>
/// </list>
/// A single lock guards the state; every operation under it is O(1) except the periodic sweep.
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, LinkedListNode<IdempotencyEntry>> _index =
        new Dictionary<string, LinkedListNode<IdempotencyEntry>>(StringComparer.Ordinal);
    private readonly LinkedList<IdempotencyEntry> _order = new LinkedList<IdempotencyEntry>();
    private readonly Dictionary<string, (string Token, DateTimeOffset Expires)> _reservations =
        new Dictionary<string, (string Token, DateTimeOffset Expires)>(StringComparer.Ordinal);
    private readonly int _maxEntries;
    private readonly TimeSpan? _defaultExpiration;
    private readonly int _sweepEvery;
    private readonly TimeProvider _time;
    private int _writesSinceSweep;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryIdempotencyStore"/>.
    /// </summary>
    /// <param name="options">Capacity and retention limits. <c>null</c> uses the defaults.</param>
    /// <param name="timeProvider">Clock used for expiry. <c>null</c> uses <see cref="TimeProvider.System"/>.</param>
    public InMemoryIdempotencyStore(InMemoryIdempotencyStoreOptions? options = null, TimeProvider? timeProvider = null)
    {
        options ??= new InMemoryIdempotencyStoreOptions();
        _maxEntries = options.MaxEntries;
        _defaultExpiration = options.DefaultExpiration;
        _sweepEvery = Math.Max(100, _maxEntries / 10);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Lookup(key));

    /// <inheritdoc />
    public Task StoreAsync(IdempotencyEntry entry, CancellationToken ct = default)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        var stored = entry.ExpiresAt is null && _defaultExpiration.HasValue
            ? new IdempotencyEntry
            {
                Key = entry.Key,
                SerializedResponse = entry.SerializedResponse,
                ResponseTypeName = entry.ResponseTypeName,
                RequestFingerprint = entry.RequestFingerprint,
                CreatedAt = entry.CreatedAt,
                ExpiresAt = _time.GetUtcNow().Add(_defaultExpiration.Value)
            }
            : entry;

        lock (_gate)
        {
            RemoveLocked(stored.Key);
            _index[stored.Key] = _order.AddLast(stored);

            if (++_writesSinceSweep >= _sweepEvery)
                SweepLocked();

            while (_index.Count > _maxEntries)
                RemoveLocked(_order.First!.Value.Key);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        lock (_gate) RemoveLocked(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => Task.FromResult(Lookup(key) is not null);

    /// <inheritdoc />
    public bool SupportsReservation => true;

    /// <inheritdoc />
    public Task<string?> TryReserveAsync(string key, TimeSpan lease, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_reservations.TryGetValue(key, out var held) && held.Expires > now)
                return Task.FromResult<string?>(null);

            var token = Guid.NewGuid().ToString("N");
            _reservations[key] = (token, now.Add(lease));
            return Task.FromResult<string?>(token);
        }
    }

    /// <inheritdoc />
    public Task ReleaseReservationAsync(string key, string token, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_reservations.TryGetValue(key, out var held) && held.Token == token)
                _reservations.Remove(key);
        }

        return Task.CompletedTask;
    }

    private IdempotencyEntry? Lookup(string key)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(key, out var node))
                return null;

            if (node.Value.IsExpiredAt(_time.GetUtcNow()))
            {
                RemoveLocked(key);
                return null;
            }

            return node.Value;
        }
    }

    private void RemoveLocked(string key)
    {
        if (_index.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _index.Remove(key);
        }
    }

    private void SweepLocked()
    {
        _writesSinceSweep = 0;
        var now = _time.GetUtcNow();
        foreach (var stale in _reservations.Where(r => r.Value.Expires <= now).Select(r => r.Key).ToList())
            _reservations.Remove(stale);

        var node = _order.First;
        while (node is not null)
        {
            var next = node.Next;
            if (node.Value.IsExpiredAt(now))
            {
                _order.Remove(node);
                _index.Remove(node.Value.Key);
            }
            node = next;
        }
    }
}
