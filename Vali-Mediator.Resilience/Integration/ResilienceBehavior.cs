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
/// </summary>
public sealed class ResilienceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Static per TRequest+TResponse combination — resolved once, shared across all behavior instances.
    private static ResiliencePolicy? _cachedPolicy;
    private static volatile bool _resolved;
    private static readonly object Lock = new();

    private readonly IResiliencePolicyProvider<TRequest>? _provider;
    private readonly IGlobalResiliencePolicyProvider? _globalProvider;

    public ResilienceBehavior(
        IEnumerable<IResiliencePolicyProvider<TRequest>> providers,
        IEnumerable<IGlobalResiliencePolicyProvider> globalProviders)
    {
        _provider = providers.FirstOrDefault();
        _globalProvider = globalProviders.FirstOrDefault();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        if (!_resolved)
        {
            lock (Lock)
            {
                if (!_resolved)
                {
                    _cachedPolicy = _provider?.GetPolicy(request)
                        ?? (request is IResilient resilient ? resilient.Policy : null)
                        ?? _globalProvider?.GetPolicy(request);
                    _resolved = true;
                }
            }
        }

        ResiliencePolicy? policy = _cachedPolicy;

        if (policy == null)
            return await next().ConfigureAwait(false);

        var initialProperties = new Dictionary<string, object?> { ["Vali.Request"] = request };

        return await policy.ExecuteAsync(
            _ => next(),
            initialProperties,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
