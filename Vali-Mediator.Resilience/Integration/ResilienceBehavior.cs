using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Integration;

/// <summary>
/// Vali-Mediator pipeline behaviour that wraps any <c>IRequest&lt;TResponse&gt;</c> with a
/// <see cref="ResiliencePolicy"/>. The policy is resolved in order:
/// <list type="number">
///   <item><see cref="IResiliencePolicyProvider{TRequest}"/> registered in DI (preferred — keeps policy out of the domain model).</item>
///   <item><see cref="IResilient"/> implemented on the request itself (legacy / simple cases).</item>
/// </list>
/// The policy is resolved on every request; a <c>null</c> result means "no resilience" and is never cached.
/// Providers that must share circuit-breaker / bulkhead state across calls have to return the same
/// <see cref="ResiliencePolicy"/> instance each time.
/// </summary>
public sealed class ResilienceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IResiliencePolicyProvider<TRequest>? _provider;
    private readonly IGlobalResiliencePolicyProvider? _globalProvider;

    /// <summary>Creates the behavior; the first registered provider of each kind is used.</summary>
    /// <param name="providers">Request-specific policy providers.</param>
    /// <param name="globalProviders">Fallback policy providers for requests without a specific one.</param>
    public ResilienceBehavior(
        IEnumerable<IResiliencePolicyProvider<TRequest>> providers,
        IEnumerable<IGlobalResiliencePolicyProvider> globalProviders)
    {
        _provider = providers.FirstOrDefault();
        _globalProvider = globalProviders.FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        Func<CancellationToken, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
#pragma warning disable CS0618 // IResilient is obsolete but still supported for existing requests
        ResiliencePolicy? policy = _provider?.GetPolicy(request)
            ?? (request is IResilient resilient ? resilient.Policy : null)
            ?? _globalProvider?.GetPolicy(request);
#pragma warning restore CS0618

        if (policy == null)
            return await next(cancellationToken).ConfigureAwait(false);

        var initialProperties = new Dictionary<string, object?> { [Vali_Mediator_Resilience.Core.Context.ResilienceContext.RequestKey] = request };

        return await policy.ExecuteAsync(
            ct => next(ct),
            initialProperties,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
