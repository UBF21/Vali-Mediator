using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Integration;

/// <summary>
/// Marker interface for Vali-Mediator request types that should be wrapped in a resilience policy.
/// Implement this on your <c>IRequest&lt;TResponse&gt;</c> classes and return a
/// <see cref="ResiliencePolicy"/> (or <see cref="ResiliencePolicy{T}"/>) from <see cref="Policy"/>.
/// </summary>
/// <example>
/// <code>
/// public class CallPaymentCommand : IRequest&lt;Result&lt;PaymentDto&gt;&gt;, IResilient
/// {
///     public ResiliencePolicy Policy => ResiliencePolicy.Create("payment")
///         .Retry(3)
///         .Timeout(TimeSpan.FromSeconds(10))
///         .Build();
/// }
/// </code>
/// </example>
public interface IResilient
{
    /// <summary>
    /// The resilience policy that wraps this request's handler invocation.
    /// Called once per request dispatch; consider caching the policy instance as a static field.
    /// </summary>
    ResiliencePolicy Policy { get; }
}
