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

    /// <summary>
    /// <c>true</c> when the store can atomically reserve a key across processes
    /// (<see cref="TryReserveAsync"/> / <see cref="ReleaseReservationAsync"/>). Defaults to <c>false</c>, which keeps
    /// the historical behavior: only a per-process lock prevents duplicate concurrent executions.
    /// </summary>
    /// <remarks>
    /// Stores shared by several instances (Redis, SQL, ...) should return <c>true</c>; without a reservation two
    /// instances that receive the same key at the same time both run the handler.
    /// </remarks>
    bool SupportsReservation => false;

    /// <summary>
    /// Atomically claims <paramref name="key"/> for <paramref name="lease"/>. Returns an opaque token when this caller
    /// won the claim, or <c>null</c> when another caller currently holds it. The lease bounds how long a crashed
    /// caller can block others; it must be longer than the handler's worst-case run time.
    /// </summary>
    /// <param name="key">The idempotency key (the same value passed to <see cref="FindAsync"/>).</param>
    /// <param name="lease">How long the reservation lives if it is never released.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> TryReserveAsync(string key, TimeSpan lease, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    /// <summary>
    /// Releases a reservation obtained with <see cref="TryReserveAsync"/>. Must only release the reservation that
    /// carries <paramref name="token"/> (a reservation that already expired and was re-acquired by someone else must
    /// be left alone).
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="token">The token returned by <see cref="TryReserveAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReleaseReservationAsync(string key, string token, CancellationToken ct = default)
        => Task.CompletedTask;
}
