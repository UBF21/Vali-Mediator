namespace Vali_Mediator_Idempotency.Core.Options;

/// <summary>
/// Capacity and retention limits for <c>InMemoryIdempotencyStore</c>.
/// </summary>
public sealed class InMemoryIdempotencyStoreOptions
{
    /// <summary>
    /// Maximum number of entries kept. When exceeded, the oldest entries are evicted first. Default: 10,000.
    /// </summary>
    public int MaxEntries
    {
        get => _maxEntries;
        set => _maxEntries = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxEntries), value, "Must be greater than zero.");
    }

    private int _maxEntries = 10_000;
    private TimeSpan? _defaultExpiration = TimeSpan.FromHours(24);

    /// <summary>
    /// Expiry applied to entries stored without one, so the store cannot grow forever.
    /// <c>null</c> keeps such entries until evicted by <see cref="MaxEntries"/>. Default: 24 hours.
    /// </summary>
    public TimeSpan? DefaultExpiration
    {
        get => _defaultExpiration;
        set => _defaultExpiration = value is null || value.Value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(DefaultExpiration), value, "Must be null or greater than zero.");
    }
}
