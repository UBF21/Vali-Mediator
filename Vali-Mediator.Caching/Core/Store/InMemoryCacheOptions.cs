namespace Vali_Mediator_Caching.Core.Store;

/// <summary>
/// Configuration options for <see cref="InMemoryCacheStore"/>.
/// </summary>
/// <remarks>
/// Every numeric limit must be greater than zero; setting an invalid value throws
/// <see cref="ArgumentOutOfRangeException"/>. The limits bound the memory the store can hold
/// even when keys and groups are derived from client-controlled input.
/// </remarks>
public class InMemoryCacheOptions
{
    private int _maxEntries = 10_000;
    private int _maxKeyLength = 512;
    private int _maxGroups = 10_000;
    private int _maxKeysPerGroup = 10_000;
    private TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);
    private TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>
    /// Gets or sets the maximum number of entries the store will hold. The limit is strict:
    /// when the store is full the least recently accessed entry is evicted to make room.
    /// Defaults to <c>10000</c>.
    /// </summary>
    public int MaxEntries
    {
        get => _maxEntries;
        set => _maxEntries = Positive(value, nameof(MaxEntries));
    }

    /// <summary>
    /// Gets or sets the maximum length of a cache key or group name. Longer keys are never
    /// stored (reads miss and writes are ignored) and a key registered under a longer group
    /// name is dropped, so oversized client-controlled keys cannot inflate memory.
    /// Defaults to <c>512</c>.
    /// </summary>
    public int MaxKeyLength
    {
        get => _maxKeyLength;
        set => _maxKeyLength = Positive(value, nameof(MaxKeyLength));
    }

    /// <summary>
    /// Gets or sets the maximum number of distinct groups indexed at the same time.
    /// A key that would open a group beyond this limit is dropped from the cache instead of
    /// being left in the store where a group invalidation could not reach it.
    /// Defaults to <c>10000</c>.
    /// </summary>
    public int MaxGroups
    {
        get => _maxGroups;
        set => _maxGroups = Positive(value, nameof(MaxGroups));
    }

    /// <summary>
    /// Gets or sets the maximum number of keys indexed under one group. A key that would exceed
    /// the limit is dropped from the cache (see <see cref="MaxGroups"/>).
    /// Defaults to <c>10000</c>.
    /// </summary>
    public int MaxKeysPerGroup
    {
        get => _maxKeysPerGroup;
        set => _maxKeysPerGroup = Positive(value, nameof(MaxKeysPerGroup));
    }

    /// <summary>
    /// Gets or sets the minimum interval between full sweeps that remove expired entries.
    /// The sweep runs lazily on writes, never on a background timer. Expired entries are
    /// also removed when they are read. Defaults to <c>5 minutes</c>.
    /// </summary>
    public TimeSpan CleanupInterval
    {
        get => _cleanupInterval;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(CleanupInterval), value, "Must be greater than zero.");
            _cleanupInterval = value;
        }
    }

    /// <summary>
    /// Gets or sets the clock used for expiration. Defaults to <see cref="TimeProvider.System"/>;
    /// replace it to make expiry deterministic in tests.
    /// </summary>
    public TimeProvider TimeProvider
    {
        get => _timeProvider;
        set => _timeProvider = value ?? throw new ArgumentNullException(nameof(TimeProvider));
    }

    private static int Positive(int value, string name)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(name, value, "Must be greater than zero.");
        return value;
    }
}
