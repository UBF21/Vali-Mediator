using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Vali_Mediator_Caching.Core.Abstractions;
using Vali_Mediator_Caching.Core.Enums;
using Vali_Mediator_Caching.Core.Interfaces;

namespace Vali_Mediator_Caching.Pipeline;

/// <summary>
/// Vali-Mediator pipeline behavior that provides transparent caching for any
/// <c>IRequest&lt;TResponse&gt;</c> that implements <see cref="ICacheable"/>.
/// </summary>
/// <remarks>
/// <para>
/// Register via <c>config.AddCachingBehavior()</c> inside <c>AddValiMediator</c>.
/// </para>
/// <para>
/// The behavior respects <see cref="ICacheable.Order"/>:
/// <list type="bullet">
///   <item><term>ReadThenWrite</term><description>
///     Read the cache first; on a miss execute the handler and write the result.
///     Concurrent misses for the same key share one execution.
///   </description></item>
///   <item><term>WriteOnly</term><description>
///     Always execute the handler and overwrite the cache entry; never read.
///   </description></item>
///   <item><term>ReadOnly</term><description>
///     Read the cache; on a miss execute the handler but do NOT write to cache.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// When <see cref="ICacheable.BypassCache"/> is <c>true</c>, the cache read is skipped
/// but the fresh result is still written (unless <see cref="CacheOrder.ReadOnly"/>).
/// </para>
/// <para>
/// Failed <see cref="IResult"/> values are never written, but concurrent callers still share the
/// single execution. See <see cref="CachingOptions.CoalescingWaitTimeout"/> for the bound on how
/// long a caller waits for it. The cache key is used as-is: include the user or tenant in
/// <see cref="ICacheable.CacheKey"/> whenever the response depends on them.
/// </para>
/// </remarks>
public sealed class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Static per closed generic type: one in-flight execution per cache key.
    private static readonly RequestCoalescer<TResponse> Coalescer = new RequestCoalescer<TResponse>();

    internal static int PendingLockCount => Coalescer.InFlightCount;

    internal static int WaitingCount => Coalescer.WaitingCount;

    private readonly ICacheStore _store;
    private readonly TimeSpan _coalescingWaitTimeout;

    /// <summary>
    /// Initializes a new instance of <see cref="CachingBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <param name="store">The cache store.</param>
    /// <param name="options">Optional behavior options; defaults apply when omitted.</param>
    public CachingBehavior(ICacheStore store, CachingOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coalescingWaitTimeout = (options ?? new CachingOptions()).CoalescingWaitTimeout;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        Func<CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheable cacheable)
            return await next(cancellationToken).ConfigureAwait(false);

        var order = cacheable.Order;
        bool shouldRead = !cacheable.BypassCache && order != CacheOrder.WriteOnly;
        if (shouldRead)
        {
            var (found, cached) = await _store
                .TryGetAsync<TResponse>(cacheable.CacheKey, cancellationToken)
                .ConfigureAwait(false);

            if (found)
                return cached!;
        }

        // Only ReadThenWrite both reads and writes, so only there can waiters reuse the result.
        if (shouldRead && order == CacheOrder.ReadThenWrite)
            return await Coalescer
                .RunAsync(
                    cacheable.CacheKey,
                    ct => ReadOrExecuteAsync(cacheable, next, ct),
                    _coalescingWaitTimeout,
                    cancellationToken)
                .ConfigureAwait(false);

        return await ExecuteAndStoreAsync(cacheable, next, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> ReadOrExecuteAsync(
        ICacheable cacheable,
        Func<CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        // Another caller may have populated the entry between our miss and becoming the leader.
        var (found, cached) = await _store
            .TryGetAsync<TResponse>(cacheable.CacheKey, cancellationToken)
            .ConfigureAwait(false);

        if (found)
            return cached!;

        return await ExecuteAndStoreAsync(cacheable, next, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> ExecuteAndStoreAsync(
        ICacheable cacheable,
        Func<CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        var result = await next(cancellationToken).ConfigureAwait(false);

        bool shouldWrite = cacheable.Order != CacheOrder.ReadOnly
                           && result is not null
                           && !(result is IResult outcome && outcome.IsFailure);
        if (shouldWrite)
        {
            await _store.SetAsync(
                    cacheable.CacheKey,
                    result,
                    cacheable.AbsoluteExpiration,
                    cacheable.SlidingExpiration,
                    cancellationToken)
                .ConfigureAwait(false);

            await RegisterGroupAsync(cacheable, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    // Stores that do not implement IGroupAwareCacheStore are expected to handle groups themselves.
    private Task RegisterGroupAsync(ICacheable cacheable, CancellationToken cancellationToken)
        => !string.IsNullOrEmpty(cacheable.CacheGroup) && _store is IGroupAwareCacheStore groupAware
            ? groupAware.RegisterKeyInGroupAsync(cacheable.CacheGroup, cacheable.CacheKey, cancellationToken)
            : Task.CompletedTask;
}
