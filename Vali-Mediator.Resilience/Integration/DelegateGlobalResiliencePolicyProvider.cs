using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Integration;

internal sealed class DelegateGlobalResiliencePolicyProvider : IGlobalResiliencePolicyProvider
{
    private readonly Func<object, ResiliencePolicy> _factory;

    internal DelegateGlobalResiliencePolicyProvider(Func<object, ResiliencePolicy> factory)
    {
        _factory = factory;
    }

    public ResiliencePolicy GetPolicy(object request) => _factory(request);
}
