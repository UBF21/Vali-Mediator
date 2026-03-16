using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;
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
/// </remarks>
public sealed class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ICacheStore _store;

    /// <summary>
    /// Initializes a new instance of <see cref="CachingBehavior{TRequest,TResponse}"/>.
    /// </summary>
    public CachingBehavior(ICacheStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheable cacheable)
            return await next().ConfigureAwait(false);

        var key = cacheable.CacheKey;
        var order = cacheable.Order;
        var bypass = cacheable.BypassCache;

        // Attempt cache read
        bool shouldRead = !bypass && order != CacheOrder.WriteOnly;
        if (shouldRead)
        {
            var (found, cached) = await _store
                .TryGetAsync<TResponse>(key, cancellationToken)
                .ConfigureAwait(false);

            if (found)
                return cached!;
        }

        // Execute handler
        var result = await next().ConfigureAwait(false);

        // Attempt cache write
        bool shouldWrite = order != CacheOrder.ReadOnly && result is not null;
        if (shouldWrite)
        {
            await _store.SetAsync(
                    key,
                    result,
                    cacheable.AbsoluteExpiration,
                    cacheable.SlidingExpiration,
                    cancellationToken)
                .ConfigureAwait(false);

            // Register key under its group for bulk invalidation
            if (!string.IsNullOrEmpty(cacheable.CacheGroup) && _store is IGroupAwareCacheStore groupAware)
                await groupAware.RegisterKeyInGroupAsync(cacheable.CacheGroup, key, cancellationToken)
                    .ConfigureAwait(false);
        }

        return result;
    }
}
