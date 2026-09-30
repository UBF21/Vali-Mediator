using Vali_Mediator_Caching.Core.Enums;

namespace Vali_Mediator_Caching.Core.Interfaces;

/// <summary>
/// Implemented by an <c>IRequest&lt;TResponse&gt;</c> to opt into the caching pipeline.
/// </summary>
/// <remarks>
/// When a request implements <see cref="ICacheable"/>, the <c>CachingBehavior</c>
/// intercepts the pipeline according to <see cref="Order"/> and the expiry settings.
/// </remarks>
public interface ICacheable
{
    /// <summary>
    /// Gets the unique cache key that identifies the result of this request.
    /// </summary>
    /// <remarks>
    /// The key is used exactly as returned. Two requests that produce the same key share the same
    /// cached response, so include the user or tenant (and any other input that changes the response)
    /// in the key; otherwise one caller can receive another caller's data. Keep the key short and
    /// bounded: the built-in store ignores keys longer than <c>InMemoryCacheOptions.MaxKeyLength</c>.
    /// The cached instance is shared by every caller, so responses should be immutable.
    /// </remarks>
    string CacheKey { get; }

    /// <summary>
    /// Gets the absolute expiry duration after which the cached entry is evicted.
    /// <c>null</c> means the entry never expires due to absolute time.
    /// </summary>
    TimeSpan? AbsoluteExpiration { get; }

    /// <summary>
    /// Gets the sliding expiry window. The entry is evicted if it has not been
    /// accessed within this duration since the last read or write.
    /// <c>null</c> means no sliding expiry.
    /// </summary>
    TimeSpan? SlidingExpiration { get; }

    /// <summary>
    /// Gets an optional group name used for bulk invalidation.
    /// All keys registered under the same group can be evicted at once via
    /// <see cref="Core.Abstractions.ICacheStore.RemoveByGroupAsync"/>.
    /// The group is only indexed by the behavior when the store implements
    /// <see cref="Core.Abstractions.IGroupAwareCacheStore"/>; other stores must
    /// associate keys with groups themselves (for example through native tagging).
    /// </summary>
    string? CacheGroup { get; }

    /// <summary>
    /// When <c>true</c>, the behavior skips the cache read and always executes the handler,
    /// then refreshes the cache entry with the new result.
    /// </summary>
    bool BypassCache { get; }

    /// <summary>
    /// Gets the caching order that controls whether to read, write, or both.
    /// Defaults to <see cref="CacheOrder.ReadThenWrite"/>.
    /// </summary>
    CacheOrder Order { get; }
}
