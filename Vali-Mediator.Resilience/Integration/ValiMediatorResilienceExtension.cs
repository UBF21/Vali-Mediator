using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator_Resilience.Core.Registry;

namespace Vali_Mediator_Resilience.Integration;

/// <summary>
/// Extension methods for registering resilience services into the Vali-Mediator DI pipeline.
/// </summary>
public static class ValiMediatorResilienceExtension
{
    /// <summary>
    /// Registers the <see cref="ResilienceBehavior{TRequest,TResponse}"/> open-generic pipeline behaviour
    /// so that any <c>IRequest&lt;TResponse&gt;</c> implementing <see cref="IResilient"/> is automatically
    /// wrapped with its declared <see cref="Vali_Mediator_Resilience.Core.Policies.ResiliencePolicy"/>.
    /// </summary>
    /// <remarks>
    /// Call this inside your <c>AddValiMediator</c> configuration lambda:
    /// <code>
    /// builder.Services.AddValiMediator(config =>
    /// {
    ///     config.RegisterServicesFromAssemblyContaining&lt;Program&gt;();
    ///     config.AddResilienceBehavior();
    /// });
    /// </code>
    /// </remarks>
    public static ValiMediatorConfiguration AddResilienceBehavior(
        this ValiMediatorConfiguration config,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        config.AddBehavior(
            typeof(Vali_Mediator.Core.General.Behavior.IPipelineBehavior<,>),
            typeof(ResilienceBehavior<,>),
            lifetime);
        return config;
    }

    /// <summary>
    /// Registers the <see cref="ICircuitBreakerRegistry"/> as a singleton in the DI container.
    /// Call this when you want all components sharing a <c>CircuitKey</c> to share the same
    /// circuit state — even across different <see cref="Core.Policies.ResiliencePolicy"/> instances.
    /// </summary>
    public static IServiceCollection AddResilienceRegistry(this IServiceCollection services)
    {
        services.AddSingleton<ICircuitBreakerRegistry, CircuitBreakerRegistry>();
        return services;
    }
}
