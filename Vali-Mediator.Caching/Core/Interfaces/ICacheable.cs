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
