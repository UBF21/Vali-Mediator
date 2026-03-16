using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Caching.Core.Abstractions;
using Vali_Mediator_Caching.Core.Interfaces;

namespace Vali_Mediator_Caching.Pipeline;

/// <summary>
/// Vali-Mediator pipeline behavior that evicts cache entries after a request that implements
/// <see cref="IInvalidatesCache"/> completes successfully.
/// </summary>
/// <remarks>
/// Register via <c>config.AddCachingBehavior()</c> inside <c>AddValiMediator</c>.
/// This behavior runs after the inner handler returns without throwing an exception.
/// If the handler throws, no invalidation occurs.
/// </remarks>
public sealed class CacheInvalidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ICacheStore _store;

    /// <summary>
    /// Initializes a new instance of <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/>.
    /// </summary>
    public CacheInvalidationBehavior(ICacheStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        var result = await next().ConfigureAwait(false);

        if (request is not IInvalidatesCache invalidator)
            return result;

        foreach (var key in invalidator.InvalidatedKeys)
            await _store.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        foreach (var group in invalidator.InvalidatedGroups)
            await _store.RemoveByGroupAsync(group, cancellationToken).ConfigureAwait(false);

        return result;
    }
}
