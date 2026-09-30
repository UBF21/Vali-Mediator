namespace Vali_Mediator_Resilience.Integration;

/// <summary>
/// Provides a resilience policy for a specific request type.
/// Register implementations in DI — ResilienceBehavior discovers them automatically.
/// This is the preferred alternative to implementing IResilient on the command itself,
/// since it keeps infrastructure concerns out of the domain model.
/// <para>
/// <see cref="GetPolicy"/> is called on every request. To share circuit-breaker / bulkhead state
/// between calls, return the same <see cref="Core.Policies.ResiliencePolicy"/> instance (e.g. build it once in a field).
/// </para>
/// </summary>
/// <example>
/// <code>
/// public class LoginCommandPolicyProvider : IResiliencePolicyProvider&lt;LoginCommand&gt;
/// {
///     public ResiliencePolicy GetPolicy(LoginCommand request) =>
///         ResiliencePolicy.Create("login")
///             .RateLimiter(opts =>
///             {
///                 opts.Algorithm = RateLimiterAlgorithm.SlidingWindow;
///                 opts.PermitLimit = 10;
///                 opts.Window = TimeSpan.FromSeconds(30);
///                 opts.PartitionKeyResolver = req => ((LoginCommand)req).UserId;
///             })
///             .Build();
/// }
///
/// // Registration
/// services.AddResiliencePolicyProvider&lt;LoginCommand, LoginCommandPolicyProvider&gt;();
/// </code>
/// </example>
public interface IResiliencePolicyProvider<TRequest>
{
    /// <summary>Returns the policy for <paramref name="request"/>; evaluated per request, never cached by the framework.</summary>
    Vali_Mediator_Resilience.Core.Policies.ResiliencePolicy GetPolicy(TRequest request);
}
