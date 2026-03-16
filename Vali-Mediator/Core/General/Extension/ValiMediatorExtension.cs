using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Processors;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Streaming;

namespace Vali_Mediator.Core.General.Extension;

/// <summary>
/// Extension methods for registering Vali-Mediator services with the DI container.
/// </summary>
public static class ValiMediatorExtension
{
    /// <summary>
    /// Adds Vali-Mediator services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">An action to configure the <see cref="ValiMediatorConfiguration"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    public static IServiceCollection AddValiMediator(
        this IServiceCollection services,
        Action<ValiMediatorConfiguration> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        var config = new ValiMediatorConfiguration();
        configure(config);

        services.AddScoped<IValiMediator, ValiMediator>();

        foreach (var (assembly, lifetime) in config.GetAssemblies())
        {
            var types = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false })
                .ToList();

            RegisterRequestHandlers(services, types, lifetime);
            RegisterNotificationHandlers(services, types, lifetime);
            RegisterFireAndForgetHandlers(services, types, lifetime);
            RegisterStreamHandlers(services, types, lifetime);
            RegisterAutoDiscoveredPreProcessors(services, types, lifetime);
            RegisterAutoDiscoveredPostProcessors(services, types, lifetime);
        }

        RegisterBehaviors(services, config);
        RegisterExplicitPreProcessors(services, config);
        RegisterExplicitPostProcessors(services, config);
        RegisterExplicitRequestPreProcessors(services, config);
        RegisterExplicitRequestPostProcessors(services, config);

        return services;
    }

    // -------------------------------------------------------------------------
    // Auto-discovered from assembly scan
    // -------------------------------------------------------------------------

    private static void RegisterRequestHandlers(
        IServiceCollection services, List<Type> types, ServiceLifetime lifetime)
    {
        foreach (var handlerType in FindImplementations(types, typeof(IRequestHandler<,>)))
        {
            var interfaceType = GetFirstInterface(handlerType, typeof(IRequestHandler<,>));
            services.Add(ServiceDescriptor.Describe(interfaceType, handlerType, lifetime));
        }
    }

    private static void RegisterNotificationHandlers(
        IServiceCollection services, List<Type> types, ServiceLifetime lifetime)
    {
        foreach (var handlerType in FindImplementations(types, typeof(INotificationHandler<>)))
        {
            var interfaceType = GetFirstInterface(handlerType, typeof(INotificationHandler<>));
            services.Add(ServiceDescriptor.Describe(interfaceType, handlerType, lifetime));
        }
    }

    private static void RegisterFireAndForgetHandlers(
        IServiceCollection services, List<Type> types, ServiceLifetime lifetime)
    {
        foreach (var handlerType in FindImplementations(types, typeof(IFireAndForgetHandler<>)))
        {
            var interfaceType = GetFirstInterface(handlerType, typeof(IFireAndForgetHandler<>));
            services.Add(ServiceDescriptor.Describe(interfaceType, handlerType, lifetime));
        }
    }

    private static void RegisterStreamHandlers(
        IServiceCollection services, List<Type> types, ServiceLifetime lifetime)
    {
        foreach (var handlerType in FindImplementations(types, typeof(IStreamRequestHandler<,>)))
        {
            var interfaceType = GetFirstInterface(handlerType, typeof(IStreamRequestHandler<,>));
            services.Add(ServiceDescriptor.Describe(interfaceType, handlerType, lifetime));
        }
    }

    private static void RegisterAutoDiscoveredPreProcessors(
        IServiceCollection services, List<Type> types, ServiceLifetime lifetime)
    {
        foreach (var processorType in FindImplementations(types, typeof(IPreProcessor<>)))
        {
            var interfaceType = GetFirstInterface(processorType, typeof(IPreProcessor<>));
            services.Add(ServiceDescriptor.Describe(interfaceType, processorType, lifetime));
        }

        foreach (var processorType in FindImplementations(types, typeof(IPreProcessor<,>)))
        {
            var interfaceType = GetFirstInterface(processorType, typeof(IPreProcessor<,>));
            services.Add(ServiceDescriptor.Describe(interfaceType, processorType, lifetime));
        }
    }

    private static void RegisterAutoDiscoveredPostProcessors(
        IServiceCollection services, List<Type> types, ServiceLifetime lifetime)
    {
        foreach (var processorType in FindImplementations(types, typeof(IPostProcessor<>)))
        {
            var interfaceType = GetFirstInterface(processorType, typeof(IPostProcessor<>));
            services.Add(ServiceDescriptor.Describe(interfaceType, processorType, lifetime));
        }

        foreach (var processorType in FindImplementations(types, typeof(IPostProcessor<,>)))
        {
            var interfaceType = GetFirstInterface(processorType, typeof(IPostProcessor<,>));
            services.Add(ServiceDescriptor.Describe(interfaceType, processorType, lifetime));
        }
    }

    // -------------------------------------------------------------------------
    // Explicit registrations from ValiMediatorConfiguration
    // -------------------------------------------------------------------------

    private static void RegisterBehaviors(IServiceCollection services, ValiMediatorConfiguration config)
    {
        foreach (var (serviceType, implementationType, lifetime) in config.GetBehaviors())
            services.Add(ServiceDescriptor.Describe(serviceType, implementationType, lifetime));
    }

    private static void RegisterExplicitPreProcessors(IServiceCollection services, ValiMediatorConfiguration config)
    {
        foreach (var (serviceType, implementationType, lifetime) in config.GetPreProcessors())
            services.Add(ServiceDescriptor.Describe(serviceType, implementationType, lifetime));
    }

    private static void RegisterExplicitPostProcessors(IServiceCollection services, ValiMediatorConfiguration config)
    {
        foreach (var (serviceType, implementationType, lifetime) in config.GetPostProcessors())
            services.Add(ServiceDescriptor.Describe(serviceType, implementationType, lifetime));
    }

    private static void RegisterExplicitRequestPreProcessors(IServiceCollection services, ValiMediatorConfiguration config)
    {
        foreach (var (serviceType, implementationType, lifetime) in config.GetRequestPreProcessors())
            services.Add(ServiceDescriptor.Describe(serviceType, implementationType, lifetime));
    }

    private static void RegisterExplicitRequestPostProcessors(IServiceCollection services, ValiMediatorConfiguration config)
    {
        foreach (var (serviceType, implementationType, lifetime) in config.GetRequestPostProcessors())
            services.Add(ServiceDescriptor.Describe(serviceType, implementationType, lifetime));
    }

    // -------------------------------------------------------------------------
    // DI convenience helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers the built-in <see cref="Vali_Mediator.Core.General.Behavior.TimeoutBehavior{TRequest,TResponse}"/>
    /// open-generic pipeline behavior.
    /// Requests that implement <see cref="Vali_Mediator.Core.Request.IHasTimeout"/> will be automatically
    /// cancelled when their declared timeout elapses.
    /// </summary>
    public static IServiceCollection AddTimeoutBehavior(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        services.Add(ServiceDescriptor.Describe(
            typeof(Vali_Mediator.Core.General.Behavior.IPipelineBehavior<,>),
            typeof(Vali_Mediator.Core.General.Behavior.TimeoutBehavior<,>),
            lifetime));
        return services;
    }

    /// <summary>
    /// Registers the <see cref="Vali_Mediator.Core.Notification.InMemoryDeadLetterQueue"/> as a singleton
    /// <see cref="Vali_Mediator.Core.Notification.IDeadLetterQueue"/>.
    /// Failed handlers during <see cref="Vali_Mediator.Core.Notification.PublishStrategy.ResilientParallel"/>
    /// publishes will be captured here instead of re-thrown.
    /// </summary>
    public static IServiceCollection AddInMemoryDeadLetterQueue(
        this IServiceCollection services,
        int maxEntries = 1_000)
    {
        services.AddSingleton<Vali_Mediator.Core.Notification.IDeadLetterQueue>(
            new Vali_Mediator.Core.Notification.InMemoryDeadLetterQueue(maxEntries));
        return services;
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static IEnumerable<Type> FindImplementations(List<Type> types, Type openGenericInterface)
        => types.Where(t => t.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface));

    private static Type GetFirstInterface(Type implementationType, Type openGenericInterface)
        => implementationType.GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface);
}
