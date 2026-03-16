using Vali_Mediator_Idempotency.Core.Models;

namespace Vali_Mediator_Idempotency.Core.Abstractions;

/// <summary>
/// Defines the persistence contract for idempotency entries.
/// </summary>
/// <remarks>
/// Implement this interface to provide a custom backing store (e.g. Redis, SQL, distributed cache).
/// The built-in <c>InMemoryIdempotencyStore</c> is registered by calling
/// <c>services.AddInMemoryIdempotencyStore()</c>.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Looks up an <see cref="IdempotencyEntry"/> by <paramref name="key"/>.
    /// Returns <c>null</c> when the key is not found or the entry has expired.
    /// </summary>
    /// <param name="key">The idempotency key to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entry, or <c>null</c> if not found or expired.</returns>
    Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Persists an <see cref="IdempotencyEntry"/> in the store.
    /// Replaces any existing entry for the same key.
    /// </summary>
    /// <param name="entry">The entry to store.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StoreAsync(IdempotencyEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes the entry identified by <paramref name="key"/> from the store.
    /// No-op if the key does not exist.
    /// </summary>
    /// <param name="key">The idempotency key to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> when a non-expired entry exists for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The idempotency key to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if a live entry exists; otherwise <c>false</c>.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
