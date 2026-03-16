using System.Collections.Concurrent;
using Vali_Mediator_Caching.Core.Abstractions;

namespace Vali_Mediator_Caching.Core.Store;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="ICacheStore"/> and
/// <see cref="IGroupAwareCacheStore"/>.
/// Uses lazy expiry checking: entries are validated on every read rather than
/// on a background timer.
/// </summary>
public sealed class InMemoryCacheStore : IGroupAwareCacheStore
{
    private sealed class CacheEntry
    {
        public object Value { get; }
        public DateTimeOffset? AbsoluteExpiry { get; }
        public TimeSpan? SlidingExpiry { get; }
        public DateTimeOffset LastAccessed { get; set; }

        public CacheEntry(
            object value,
            DateTimeOffset? absoluteExpiry,
            TimeSpan? slidingExpiry)
        {
            Value = value;
            AbsoluteExpiry = absoluteExpiry;
            SlidingExpiry = slidingExpiry;
            LastAccessed = DateTimeOffset.UtcNow;
        }

        public bool IsExpired(DateTimeOffset now)
        {
            if (AbsoluteExpiry.HasValue && now >= AbsoluteExpiry.Value)
                return true;

            if (SlidingExpiry.HasValue && now - LastAccessed >= SlidingExpiry.Value)
                return true;

            return false;
        }
    }

    private readonly ConcurrentDictionary<string, CacheEntry> _cache
        = new ConcurrentDictionary<string, CacheEntry>(StringComparer.Ordinal);

    // group -> set of keys
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _groups
        = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.Ordinal);

    private readonly InMemoryCacheOptions _options;

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
    }

    /// <inheritdoc />
    public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(key, out var entry))
            return Task.FromResult((false, default(T)));

        var now = DateTimeOffset.UtcNow;
        if (entry.IsExpired(now))
        {
            _cache.TryRemove(key, out _);
            return Task.FromResult((false, default(T)));
        }

        // Update last-accessed for sliding expiry
        entry.LastAccessed = now;

        if (entry.Value is T typed)
            return Task.FromResult((true, (T?)typed));

        // Type mismatch — treat as miss
        return Task.FromResult((false, default(T)));
    }

    /// <inheritdoc />
    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? absoluteExpiration,
        TimeSpan? slidingExpiration,
        CancellationToken ct = default)
    {
        if (value is null)
            return Task.CompletedTask;

        // Respect MaxEntries: do not add when at capacity (existing keys can be updated)
        if (!_cache.ContainsKey(key) && _cache.Count >= _options.MaxEntries)
            return Task.CompletedTask;

        var now = DateTimeOffset.UtcNow;
        var absoluteExpiry = absoluteExpiration.HasValue
            ? now + absoluteExpiration.Value
            : (DateTimeOffset?)null;

        var entry = new CacheEntry(value, absoluteExpiry, slidingExpiration);
        _cache[key] = entry;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveByGroupAsync(string group, CancellationToken ct = default)
    {
        if (!_groups.TryGetValue(group, out var keys))
            return Task.CompletedTask;

        foreach (var key in keys.Keys)
            _cache.TryRemove(key, out _);

        // Clear the group bucket too
        _groups.TryRemove(group, out _);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RegisterKeyInGroupAsync(string group, string key, CancellationToken ct = default)
    {
        var bucket = _groups.GetOrAdd(
            group,
            _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));

        bucket.TryAdd(key, 0);
        return Task.CompletedTask;
    }
}
