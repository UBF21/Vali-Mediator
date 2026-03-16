namespace Vali_Mediator_Caching.Core.Abstractions;

/// <summary>
/// Optional extension of <see cref="ICacheStore"/> for stores that support explicit
/// group-to-key registration.
/// </summary>
/// <remarks>
/// When <see cref="CachingBehavior"/> writes a result whose request specifies a
/// <c>CacheGroup</c>, it checks whether the underlying <see cref="ICacheStore"/>
/// also implements this interface and, if so, calls <see cref="RegisterKeyInGroupAsync"/>
/// so the store can associate the key with the group for bulk invalidation via
/// <see cref="ICacheStore.RemoveByGroupAsync"/>.
///
/// Implementors that maintain an internal group index (such as
/// <see cref="Store.InMemoryCacheStore"/>) should implement this interface.
/// External stores backed by a technology that natively supports tagging or grouping
/// (e.g., Redis with tag-based eviction) may implement the association directly
/// inside <see cref="ICacheStore.SetAsync"/> and do not need this interface.
/// </remarks>
public interface IGroupAwareCacheStore : ICacheStore
{
    /// <summary>
    /// Associates <paramref name="key"/> with <paramref name="group"/> so that a
    /// subsequent call to <see cref="ICacheStore.RemoveByGroupAsync"/> evicts it.
    /// </summary>
    /// <param name="group">The group name.</param>
    /// <param name="key">The cache key to register under the group.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RegisterKeyInGroupAsync(string group, string key, CancellationToken ct = default);
}
