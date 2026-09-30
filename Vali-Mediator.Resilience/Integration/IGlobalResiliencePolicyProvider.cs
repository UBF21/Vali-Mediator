using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Integration;

/// <summary>
/// Provides a default resilience policy applied to every request that has no
/// command-specific <see cref="IResiliencePolicyProvider{TRequest}"/> or <see cref="IResilient"/>.
/// Register a single implementation via <c>services.AddGlobalResiliencePolicy(...)</c>.
/// </summary>
public interface IGlobalResiliencePolicyProvider
{
    /// <summary>Returns the policy for <paramref name="request"/>, or <c>null</c> to run it without resilience.</summary>
    /// <param name="request">The request being dispatched.</param>
    ResiliencePolicy GetPolicy(object request);
}
