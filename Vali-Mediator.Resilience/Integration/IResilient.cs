using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Integration;

/// <summary>
/// Marker interface for applying a resilience policy directly on a request class.
/// </summary>
/// <remarks>
/// <b>Deprecated.</b> Putting the policy on the command mixes infrastructure with domain data
/// — the <c>Policy</c> property gets serialized alongside command fields.
/// Prefer <see cref="IResiliencePolicyProvider{TRequest}"/> registered via
/// <c>services.AddResiliencePolicy&lt;TRequest&gt;(...)</c> instead.
/// This interface is kept only for backward compatibility.
/// </remarks>
[Obsolete("Use services.AddResiliencePolicy<TRequest>() or IResiliencePolicyProvider<TRequest> instead. " +
          "IResilient puts infrastructure concerns inside the command object.")]
public interface IResilient
{
    /// <summary>
    /// The resilience policy that wraps this request's handler invocation.
    /// Called once per request dispatch; consider caching the policy instance as a static field.
    /// </summary>
    ResiliencePolicy Policy { get; }
}
