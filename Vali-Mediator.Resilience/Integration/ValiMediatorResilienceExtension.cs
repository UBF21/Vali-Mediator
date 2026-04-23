using System.Reflection;
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
    /// Registers a resilience policy for <typeparamref name="TRequest"/> via an inline factory lambda.
    /// No class needed — ideal for simple policies defined at startup.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddResiliencePolicy&lt;LoginCommand&gt;(req =>
    ///     ResiliencePolicy.Create("login")
    ///         .RateLimiter(opts =>
    ///         {
    ///             opts.Algorithm = RateLimiterAlgorithm.SlidingWindow;
    ///             opts.PermitLimit = 10;
    ///             opts.Window = TimeSpan.FromSeconds(30);
    ///             opts.PartitionKeyResolver = r => ((LoginCommand)r).UserId;
    ///         })
    ///         .Build());
    /// </code>
    /// </example>
    public static IServiceCollection AddResiliencePolicy<TRequest>(
        this IServiceCollection services,
        Func<TRequest, Core.Policies.ResiliencePolicy> policyFactory)
    {
        services.AddSingleton<IResiliencePolicyProvider<TRequest>>(
            new DelegateResiliencePolicyProvider<TRequest>(policyFactory));
        return services;
    }

    /// <summary>
    /// Registers a <see cref="IResiliencePolicyProvider{TRequest}"/> class in DI.
    /// Use this when the provider needs injected dependencies (e.g. IOptions, ILogger).
    /// For simple cases without dependencies, prefer <see cref="AddResiliencePolicy{TRequest}"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddResiliencePolicyProvider&lt;LoginCommand, LoginCommandPolicyProvider&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddResiliencePolicyProvider<TRequest, TProvider>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TProvider : class, IResiliencePolicyProvider<TRequest>
    {
        services.Add(new ServiceDescriptor(
            typeof(IResiliencePolicyProvider<TRequest>),
            typeof(TProvider),
            lifetime));
        return services;
    }

    /// <summary>
    /// Registers a global resilience policy applied to every request that has no command-specific policy.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddGlobalResiliencePolicy(
    ///     ResiliencePolicy.Create()
    ///         .Retry(3)
    ///         .Timeout(TimeSpan.FromSeconds(30))
    ///         .Build());
    /// </code>
    /// </example>
    public static IServiceCollection AddGlobalResiliencePolicy(
        this IServiceCollection services,
        Core.Policies.ResiliencePolicy policy)
    {
        services.AddSingleton<IGlobalResiliencePolicyProvider>(
            new DelegateGlobalResiliencePolicyProvider(_ => policy));
        return services;
    }

    /// <summary>
    /// Registers a global resilience policy via a factory that receives the request instance.
    /// Useful when the global policy depends on the type or properties of the request.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddGlobalResiliencePolicy(req =>
    ///     ResiliencePolicy.Create()
    ///         .Retry(req is IQuery ? 3 : 1)
    ///         .Build());
    /// </code>
    /// </example>
    public static IServiceCollection AddGlobalResiliencePolicy(
        this IServiceCollection services,
        Func<object, Core.Policies.ResiliencePolicy> policyFactory)
    {
        services.AddSingleton<IGlobalResiliencePolicyProvider>(
            new DelegateGlobalResiliencePolicyProvider(policyFactory));
        return services;
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

    /// <summary>
    /// Auto-discovers and registers all <see cref="IResiliencePolicyProvider{TRequest}"/> implementations
    /// from the assembly containing <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// Call this after <c>AddValiMediator</c>:
    /// <code>
    /// builder.Services.AddValiMediator(config =>
    /// {
    ///     config.RegisterServicesFromAssemblyContaining&lt;Program&gt;();
    ///     config.AddResilienceBehavior();
    /// });
    ///
    /// builder.Services.RegisterResiliencePoliciesFromAssemblyContaining&lt;Program&gt;();
    /// </code>
    /// </remarks>
    /// <typeparam name="T">Any type in the target assembly.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for discovered policy providers.
    /// Defaults to <see cref="ServiceLifetime.Scoped"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection RegisterResiliencePoliciesFromAssemblyContaining<T>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        => RegisterResiliencePoliciesFromAssembly(services, typeof(T).Assembly, lifetime);

    /// <summary>
    /// Auto-discovers and registers all <see cref="IResiliencePolicyProvider{TRequest}"/> implementations
    /// from the specified assembly.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="assembly">The assembly to scan for policy providers.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for discovered policy providers.
    /// Defaults to <see cref="ServiceLifetime.Scoped"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="assembly"/> is null.</exception>
    public static IServiceCollection RegisterResiliencePoliciesFromAssembly(
        this IServiceCollection services,
        Assembly assembly,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));

        var providerInterfaceType = typeof(IResiliencePolicyProvider<>);
        var types = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .ToList();

        foreach (var implementationType in FindResiliencePolicyProviders(types))
        {
            var interfaceType = GetFirstResiliencePolicyProviderInterface(implementationType);
            if (interfaceType is not null)
                services.Add(ServiceDescriptor.Describe(interfaceType, implementationType, lifetime));
        }

        return services;
    }

    private static List<Type> FindResiliencePolicyProviders(List<Type> types)
    {
        var providerInterfaceType = typeof(IResiliencePolicyProvider<>);
        return types
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == providerInterfaceType))
            .ToList();
    }

    private static Type? GetFirstResiliencePolicyProviderInterface(Type implementationType)
    {
        var providerInterfaceType = typeof(IResiliencePolicyProvider<>);
        return implementationType
            .GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == providerInterfaceType);
    }
}
