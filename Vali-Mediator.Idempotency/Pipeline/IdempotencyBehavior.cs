using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Vali_Mediator_Idempotency.Core.Abstractions;
using Vali_Mediator_Idempotency.Core.Exceptions;
using Vali_Mediator_Idempotency.Core.Interfaces;
using Vali_Mediator_Idempotency.Core.Models;
using Vali_Mediator_Idempotency.Core.Options;
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
///   <item>Acquires a per-key <see cref="SemaphoreSlim"/> to prevent duplicate concurrent executions in this process.</item>
///   <item>Checks the store; if a live (non-expired) entry exists, deserializes and returns it.</item>
///   <item>When the store <see cref="IIdempotencyStore.SupportsReservation"/>, atomically reserves the key so other
///   instances sharing the store wait for this execution instead of repeating it.</item>
///   <item>Otherwise invokes the handler, serializes the result, and stores it with the requested expiry.</item>
/// </list>
/// The store key combines the request type, <see cref="IIdempotent.IdempotencyScope"/> and
/// <see cref="IIdempotent.IdempotencyKey"/>, so different users or request types never share a response.
/// When <see cref="IdempotencyOptions.VerifyRequestFingerprint"/> is on, reusing a key with a different payload
/// yields a <c>Conflict</c> failure (for <c>IResult</c> responses) or <see cref="IdempotencyConflictException"/>.
/// Only successful responses are stored; failed <c>Result</c> values are returned but never replayed.
/// If another instance still holds the reservation after <see cref="IdempotencyOptions.ReservationWaitTimeout"/>, the
/// caller gets a <c>Conflict</c> failure (or <see cref="IdempotencyInProgressException"/>) and may retry.
/// </remarks>
public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private sealed class KeyLock
    {
        public readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
        public int RefCount;
    }

    // Guarded by lock(KeyLocks). Entries are removed when the last waiter leaves, so it never grows.
    // In-process only: across instances the store reservation (IIdempotencyStore.SupportsReservation) coordinates.
    private static readonly Dictionary<string, KeyLock> KeyLocks = new Dictionary<string, KeyLock>(StringComparer.Ordinal);

    private static readonly Regex AssemblyQualification = new Regex(
        @",\s*[^,\[\]]+,\s*Version=[^,\]]+,\s*Culture=[^,\]]+(,\s*PublicKeyToken=[^,\]]+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string ResponseTypeName =
        NormalizeTypeName(typeof(TResponse).AssemblyQualifiedName ?? typeof(TResponse).FullName ?? typeof(TResponse).Name);

    // Removes assembly versions so stored entries survive package/assembly upgrades.
    private static string NormalizeTypeName(string typeName)
        => AssemblyQualification.Replace(typeName, string.Empty);

    internal static int ActiveLockCount
    {
        get { lock (KeyLocks) return KeyLocks.Count; }
    }

    private static KeyLock AcquireRef(string key)
    {
        lock (KeyLocks)
        {
            if (!KeyLocks.TryGetValue(key, out var keyLock))
                KeyLocks[key] = keyLock = new KeyLock();
            keyLock.RefCount++;
            return keyLock;
        }
    }

    private static void ReleaseRef(string key, KeyLock keyLock)
    {
        lock (KeyLocks)
        {
            if (--keyLock.RefCount == 0)
                KeyLocks.Remove(key);
        }
    }

    private readonly IIdempotencyStore _store;
    private readonly IIdempotencySerializer _serializer;
    private readonly IdempotencyOptions _options;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of <see cref="IdempotencyBehavior{TRequest, TResponse}"/>.
    /// </summary>
    /// <param name="store">The idempotency store used to persist and retrieve entries.</param>
    /// <param name="serializer">The serializer used to serialize/deserialize responses.</param>
    /// <param name="options">Key-length, fingerprint and reservation settings. <c>null</c> uses the defaults.</param>
    /// <param name="timeProvider">Clock used for expiry. <c>null</c> uses <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="IdempotencyOptions.MaxKeyLength"/> is not positive.</exception>
    public IdempotencyBehavior(
        IIdempotencyStore store,
        IIdempotencySerializer serializer,
        IdempotencyOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _options = options ?? new IdempotencyOptions();
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        Func<CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        if (request is not IIdempotent idempotent)
            return await next(cancellationToken).ConfigureAwait(false);

        var key = BuildKey(idempotent);
        var keyLock = AcquireRef(key);
        try
        {
            await keyLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await HandleLockedAsync(request, idempotent, key, next, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                keyLock.Semaphore.Release();
            }
        }
        finally
        {
            ReleaseRef(key, keyLock);
        }
    }

    private async Task<TResponse> HandleLockedAsync(
        TRequest request,
        IIdempotent idempotent,
        string key,
        Func<CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        var fingerprint = ComputeFingerprint(request);

        var replay = await TryReplayAsync(key, idempotent, fingerprint, cancellationToken).ConfigureAwait(false);
        if (replay.Done)
            return replay.Response!;

        string? token = null;
        if (_store.SupportsReservation)
        {
            var reservation = await ReserveAsync(key, idempotent, fingerprint, cancellationToken).ConfigureAwait(false);
            if (reservation.Done)
                return reservation.Response!;
            token = reservation.Token;
        }

        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);

            if (response is IResult outcome && outcome.IsFailure)
                return response;

            var now = _time.GetUtcNow();
            var entry = new IdempotencyEntry
            {
                Key = key,
                SerializedResponse = _serializer.Serialize(response),
                ResponseTypeName = ResponseTypeName,
                RequestFingerprint = fingerprint,
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
            // Always let go, even when the caller cancelled, so a retry is never blocked until the lease expires.
            if (token is not null)
                await _store.ReleaseReservationAsync(key, token, CancellationToken.None).ConfigureAwait(false);
        }
    }

    // Done = the stored answer (or a payload conflict) must be returned as is.
    private async Task<(bool Done, TResponse? Response)> TryReplayAsync(
        string key, IIdempotent idempotent, string fingerprint, CancellationToken cancellationToken)
    {
        var existing = await _store.FindAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing == null || existing.IsExpiredAt(_time.GetUtcNow())
            || NormalizeTypeName(existing.ResponseTypeName) != ResponseTypeName)
            return (false, default);

        if (fingerprint.Length > 0 && existing.RequestFingerprint.Length > 0
            && existing.RequestFingerprint != fingerprint)
            return (true, Conflict(idempotent.IdempotencyKey));

        return (true, _serializer.Deserialize<TResponse>(existing.SerializedResponse)!);
    }

    // Claims the key across instances. While another instance holds it, waits for its answer (replayed) or for the
    // reservation to be freed (then tries again); gives up with an "in progress" conflict after the wait timeout.
    private async Task<(bool Done, TResponse? Response, string? Token)> ReserveAsync(
        string key, IIdempotent idempotent, string fingerprint, CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        while (true)
        {
            var token = await _store.TryReserveAsync(key, _options.ReservationLease, cancellationToken).ConfigureAwait(false);
            if (token is not null)
            {
                // The previous holder may have stored its answer right before releasing.
                var late = await TryReplayAsync(key, idempotent, fingerprint, CancellationToken.None).ConfigureAwait(false);
                if (!late.Done)
                    return (false, default, token);

                await _store.ReleaseReservationAsync(key, token, CancellationToken.None).ConfigureAwait(false);
                return (true, late.Response, null);
            }

            if (_time.GetElapsedTime(started) >= _options.ReservationWaitTimeout)
                return (true, InProgress(idempotent.IdempotencyKey), null);

            await Task.Delay(_options.ReservationPollInterval, cancellationToken).ConfigureAwait(false);

            var replay = await TryReplayAsync(key, idempotent, fingerprint, cancellationToken).ConfigureAwait(false);
            if (replay.Done)
                return (true, replay.Response, null);
        }
    }

    // Length-prefixing the scope makes the key unambiguous, so "a:b"+"c" can never equal "a"+"b:c".
    private string BuildKey(IIdempotent idempotent)
    {
        var id = idempotent.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("IdempotencyKey must not be null or empty.", nameof(idempotent));
        if (id.Length > _options.MaxKeyLength)
            throw new ArgumentException(
                $"IdempotencyKey exceeds the maximum length of {_options.MaxKeyLength} characters.", nameof(idempotent));

        var scope = idempotent.IdempotencyScope ?? string.Empty;
        if (scope.Length > _options.MaxKeyLength)
            throw new ArgumentException(
                $"IdempotencyScope exceeds the maximum length of {_options.MaxKeyLength} characters.", nameof(idempotent));

        return typeof(TRequest).FullName + "#" + scope.Length + ":" + scope + "#" + id;
    }

    // ponytail: a request that cannot be serialized is not fingerprinted; add a custom fingerprint hook
    // if payload verification must be mandatory for such types.
    private string ComputeFingerprint(TRequest request)
    {
        if (!_options.VerifyRequestFingerprint)
            return string.Empty;

        try
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(_serializer.Serialize(request)));
        }
        catch (Exception ex) when (ex is NotSupportedException || ex is System.Text.Json.JsonException || ex is InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static TResponse Conflict(string idempotencyKey)
        => Rejection(
            $"Idempotency key '{idempotencyKey}' was already used with a different request payload.",
            () => new IdempotencyConflictException(idempotencyKey));

    private static TResponse InProgress(string idempotencyKey)
        => Rejection(
            $"Idempotency key '{idempotencyKey}' is still being processed by another request.",
            () => new IdempotencyInProgressException(idempotencyKey));

    private static TResponse Rejection(string message, Func<Exception> exception)
    {
        var responseType = typeof(TResponse);

        if (responseType == typeof(Result))
            return (TResponse)(object)Result.Fail(message, ErrorType.Conflict);

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var fail = responseType.GetMethod("Fail", new[] { typeof(string), typeof(ErrorType) })!;
            return (TResponse)fail.Invoke(null, new object[] { message, ErrorType.Conflict })!;
        }

        throw exception();
    }
}
