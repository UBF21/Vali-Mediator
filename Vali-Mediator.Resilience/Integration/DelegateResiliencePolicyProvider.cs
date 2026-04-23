using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Integration;

internal sealed class DelegateResiliencePolicyProvider<TRequest> : IResiliencePolicyProvider<TRequest>
{
    private readonly Func<TRequest, ResiliencePolicy> _factory;
    private readonly object _lock = new();
    private ResiliencePolicy? _cached;

    internal DelegateResiliencePolicyProvider(Func<TRequest, ResiliencePolicy> factory)
    {
        _factory = factory;
    }

    public ResiliencePolicy GetPolicy(TRequest request)
    {
        if (_cached is not null) return _cached;
        lock (_lock)
        {
            _cached ??= _factory(request);
        }
        return _cached;
    }
}
