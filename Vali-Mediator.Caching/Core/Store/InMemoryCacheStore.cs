using Vali_Mediator_Caching.Core.Abstractions;

namespace Vali_Mediator_Caching.Core.Store;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="ICacheStore"/> and
/// <see cref="IGroupAwareCacheStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// Entries are kept in a least-recently-used list, so reads, writes and evictions are O(1) and
/// <see cref="InMemoryCacheOptions.MaxEntries"/> is a strict bound. Expired entries are removed
/// when they are read and by a lazy sweep on writes at most once per
/// <see cref="InMemoryCacheOptions.CleanupInterval"/>; there is no background timer.
/// </para>
/// <para>
/// The store keeps and returns the same instance that was written, so callers must treat cached
/// values as immutable: a mutation is visible to every other consumer of the entry.
/// </para>
/// <para>
/// A key belongs to at most one group. Registering it under another group moves it. Group limits
/// (<see cref="InMemoryCacheOptions.MaxGroups"/>, <see cref="InMemoryCacheOptions.MaxKeysPerGroup"/>)
/// drop the entry rather than leave it in a state where group invalidation cannot reach it.
/// </para>
/// </remarks>
public sealed class InMemoryCacheStore : IGroupAwareCacheStore
{
    private sealed class CacheEntry
    {
        public CacheEntry(
            string key,
            object value,
            DateTimeOffset? absoluteExpiry,
            TimeSpan? slidingExpiry,
            DateTimeOffset now)
        {
            Key = key;
            Value = value;
            AbsoluteExpiry = absoluteExpiry;
            SlidingExpiry = slidingExpiry;
            LastAccessed = now;
        }

        public string Key { get; }
        public object Value { get; }
        public DateTimeOffset? AbsoluteExpiry { get; }
        public TimeSpan? SlidingExpiry { get; }
        public DateTimeOffset LastAccessed { get; set; }
        public string? Group { get; set; }

        public bool IsExpired(DateTimeOffset now)
        {
            if (AbsoluteExpiry.HasValue && now >= AbsoluteExpiry.Value)
                return true;

            return SlidingExpiry.HasValue && now - LastAccessed >= SlidingExpiry.Value;
        }
    }

    // ponytail: one lock for the whole store keeps MaxEntries and the group index exact;
    // shard by key hash if contention shows up in profiling.
    private readonly object _sync = new object();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map
        = new Dictionary<string, LinkedListNode<CacheEntry>>(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _lru = new LinkedList<CacheEntry>(); // First = most recently used
    private readonly GroupIndex _groups = new GroupIndex();
    private readonly InMemoryCacheOptions _options;
    private readonly TimeProvider _time;
    private DateTimeOffset _nextSweep;

    internal int Count
    {
        get { lock (_sync) return _map.Count; }
    }

    internal int GroupCount
    {
        get { lock (_sync) return _groups.GroupCount; }
    }

    internal int GroupedKeyCount
    {
        get { lock (_sync) return _groups.KeyCount; }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryCacheStore"/> with default options.
    /// </summary>
    public InMemoryCacheStore() : this(new InMemoryCacheOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryCacheStore"/> with the supplied options.
    /// </summary>
    public InMemoryCacheStore(InMemoryCacheOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = options.TimeProvider;
        _nextSweep = _time.GetUtcNow() + options.CleanupInterval;
    }

    /// <inheritdoc />
    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
    {
        if (key.Length > _options.MaxKeyLength)
            return Task.FromResult((false, default(T)));

        lock (_sync)
        {
            if (!_map.TryGetValue(key, out var node))
                return Task.FromResult((false, default(T)));

            var now = _time.GetUtcNow();
            var entry = node.Value;
            if (entry.IsExpired(now))
            {
                RemoveNode(node);
                return Task.FromResult((false, default(T)));
            }

            entry.LastAccessed = now;
            _lru.Remove(node);
            _lru.AddFirst(node);

            if (entry.Value is T typed)
                return Task.FromResult((true, (T?)typed));

            // Type mismatch — treat as miss
            return Task.FromResult((false, default(T)));
        }
    }

    /// <inheritdoc />
    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? absoluteExpiration,
        TimeSpan? slidingExpiration,
        CancellationToken ct = default)
    {
        if (value is null || key.Length > _options.MaxKeyLength)
            return Task.CompletedTask;

        lock (_sync)
        {
            var now = _time.GetUtcNow();
            SweepIfDue(now);

            var entry = new CacheEntry(
                key,
                value,
                absoluteExpiration.HasValue ? now + absoluteExpiration.Value : (DateTimeOffset?)null,
                slidingExpiration,
                now);

            if (_map.TryGetValue(key, out var existing))
            {
                // Overwrite in place: the key keeps its group membership.
                entry.Group = existing.Value.Group;
                existing.Value = entry;
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return Task.CompletedTask;
            }

            if (_map.Count >= _options.MaxEntries)
                RemoveNode(_lru.Last!);

            _map[key] = _lru.AddFirst(entry);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var node))
                RemoveNode(node);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveByGroupAsync(string group, CancellationToken ct = default)
    {
        lock (_sync)
        {
            var keys = _groups.Take(group);
            if (keys is null)
                return Task.CompletedTask;

            foreach (var key in keys)
            {
                if (!_map.TryGetValue(key, out var node))
                    continue;

                node.Value.Group = null; // the bucket is already gone
                RemoveNode(node);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Registering a key that is not currently stored is a no-op, so an entry that was evicted
    /// between the write and the registration never leaves an orphan in the group index.
    /// </remarks>
    public Task RegisterKeyInGroupAsync(string group, string key, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (!_map.TryGetValue(key, out var node))
                return Task.CompletedTask;

            var entry = node.Value;
            if (string.Equals(entry.Group, group, StringComparison.Ordinal))
                return Task.CompletedTask;

            if (entry.Group is not null)
            {
                _groups.Remove(entry.Group, key);
                entry.Group = null;
            }

            if (group.Length > _options.MaxKeyLength
                || !_groups.TryAdd(group, key, _options.MaxGroups, _options.MaxKeysPerGroup))
            {
                // Cannot index the key: cache nothing rather than cache something no invalidation can reach.
                RemoveNode(node);
                return Task.CompletedTask;
            }

            entry.Group = group;
        }

        return Task.CompletedTask;
    }

    // Caller holds _sync.
    private void RemoveNode(LinkedListNode<CacheEntry> node)
    {
        var entry = node.Value;
        _lru.Remove(node);
        _map.Remove(entry.Key);
        if (entry.Group is not null)
            _groups.Remove(entry.Group, entry.Key);
    }

    // Caller holds _sync. Full O(n) pass, but at most once per CleanupInterval.
    private void SweepIfDue(DateTimeOffset now)
    {
        if (now < _nextSweep)
            return;

        _nextSweep = now + _options.CleanupInterval;

        var node = _lru.First;
        while (node is not null)
        {
            var next = node.Next;
            if (node.Value.IsExpired(now))
                RemoveNode(node);
            node = next;
        }
    }
}
