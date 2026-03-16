namespace Vali_Mediator_Caching.Core.Abstractions;

/// <summary>
/// Pluggable cache store abstraction used by the Vali-Mediator caching pipeline.
/// </summary>
/// <remarks>
/// Implement this interface and register it via <c>services.AddCacheStore&lt;TStore&gt;()</c>
/// to replace the built-in <see cref="Vali_Mediator_Caching.Core.Store.InMemoryCacheStore"/>.
/// </remarks>
public interface ICacheStore
{
    /// <summary>
    /// Attempts to retrieve a cached value by <paramref name="key"/>.
    /// </summary>
    /// <typeparam name="T">The expected type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple where <c>Found</c> is <c>true</c> and <c>Value</c> contains the cached
    /// item when the key exists and has not expired; otherwise <c>Found</c> is <c>false</c>
    /// and <c>Value</c> is <c>default</c>.
    /// </returns>
    Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> with the specified expiry settings.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="absoluteExpiration">
    /// Duration from now after which the entry is considered expired.
    /// Pass <c>null</c> for no absolute expiry.
    /// </param>
    /// <param name="slidingExpiration">
    /// Window of inactivity after which the entry is considered expired.
    /// Pass <c>null</c> for no sliding expiry.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? absoluteExpiration,
        TimeSpan? slidingExpiration,
        CancellationToken ct = default);

    /// <summary>
    /// Removes the entry identified by <paramref name="key"/> from the store.
    /// A missing key is silently ignored.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes all cache entries that belong to the specified <paramref name="group"/>.
    /// </summary>
    /// <param name="group">The group name whose keys should be evicted.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveByGroupAsync(string group, CancellationToken ct = default);
}
