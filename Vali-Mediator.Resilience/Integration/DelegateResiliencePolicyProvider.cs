using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Integration;

internal sealed class DelegateResiliencePolicyProvider<TRequest> : IResiliencePolicyProvider<TRequest>
{
    private readonly Func<TRequest, ResiliencePolicy> _factory;

    internal DelegateResiliencePolicyProvider(Func<TRequest, ResiliencePolicy> factory)
    {
        _factory = factory;
    }

    public ResiliencePolicy GetPolicy(TRequest request) => _factory(request);
}
