using System.Collections.Concurrent;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Idempotency.Core.Abstractions;
using Vali_Mediator_Idempotency.Core.Interfaces;
using Vali_Mediator_Idempotency.Core.Models;
using Vali_Mediator_Idempotency.Core.Serialization;

namespace Vali_Mediator_Idempotency.Pipeline;

/// <summary>
/// Pipeline behavior that enforces idempotency for requests implementing <see cref="IIdempotent"/>.
/// </summary>
/// <typeparam name="TRequest">The request type, which must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// For non-idempotent requests the behavior is a transparent pass-through.
/// For idempotent requests:
/// <list type="number">
///   <item>Acquires a per-key <see cref="SemaphoreSlim"/> to prevent duplicate concurrent executions.</item>
///   <item>Checks the store; if a live (non-expired) entry exists, deserializes and returns it.</item>
///   <item>Otherwise invokes the handler, serializes the result, and stores it with the requested expiry.</item>
/// </list>
/// </remarks>
public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks =
        new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

    private readonly IIdempotencyStore _store;
    private readonly IIdempotencySerializer _serializer;

    /// <summary>
    /// Initializes a new instance of <see cref="IdempotencyBehavior{TRequest, TResponse}"/>.
    /// </summary>
    /// <param name="store">The idempotency store used to persist and retrieve entries.</param>
    /// <param name="serializer">The serializer used to serialize/deserialize responses.</param>
    public IdempotencyBehavior(IIdempotencyStore store, IIdempotencySerializer serializer)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        if (request is not IIdempotent idempotent)
            return await next().ConfigureAwait(false);

        var key = idempotent.IdempotencyKey;
        var semaphore = KeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await _store.FindAsync(key, cancellationToken).ConfigureAwait(false);
            if (existing != null && !existing.IsExpired)
            {
                var cached = _serializer.Deserialize<TResponse>(existing.SerializedResponse);
                return cached!;
            }

            var response = await next().ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            var entry = new IdempotencyEntry
            {
                Key = key,
                SerializedResponse = _serializer.Serialize(response),
                ResponseTypeName = typeof(TResponse).AssemblyQualifiedName ?? typeof(TResponse).FullName ?? typeof(TResponse).Name,
                CreatedAt = now,
                ExpiresAt = idempotent.Expiration.HasValue
                    ? now.Add(idempotent.Expiration.Value)
                    : null
            };

            await _store.StoreAsync(entry, cancellationToken).ConfigureAwait(false);

            return response;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
