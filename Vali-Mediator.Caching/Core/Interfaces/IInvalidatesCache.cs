namespace Vali_Mediator_Caching.Core.Interfaces;

/// <summary>
/// Implemented by a command (or any <c>IRequest</c>) that should evict one or more
/// cache entries after it executes successfully.
/// </summary>
/// <remarks>
/// The <c>CacheInvalidationBehavior</c> checks for this interface after the handler returns
/// and removes all listed keys and groups from the <see cref="Core.Abstractions.ICacheStore"/>.
/// </remarks>
public interface IInvalidatesCache
{
    /// <summary>
    /// Gets the specific cache keys to evict after this request completes.
    /// </summary>
    IReadOnlyList<string> InvalidatedKeys { get; }

    /// <summary>
    /// Gets the group names whose associated keys should all be evicted after this
    /// request completes.
    /// </summary>
    IReadOnlyList<string> InvalidatedGroups { get; }
}
