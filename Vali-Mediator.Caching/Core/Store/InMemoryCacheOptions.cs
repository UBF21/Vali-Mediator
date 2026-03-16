namespace Vali_Mediator_Caching.Core.Store;

/// <summary>
/// Configuration options for <see cref="InMemoryCacheStore"/>.
/// </summary>
public class InMemoryCacheOptions
{
    /// <summary>
    /// Gets or sets the maximum number of entries the store will hold before
    /// new writes silently no-op. Defaults to <c>1000</c>.
    /// </summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the interval at which a background sweep can run to
    /// proactively remove expired entries. Defaults to <c>5 minutes</c>.
    /// </summary>
    /// <remarks>
    /// The current implementation uses lazy cleanup on access rather than a
    /// dedicated timer, so this value is reserved for future active-cleanup
    /// implementations.
    /// </remarks>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}
