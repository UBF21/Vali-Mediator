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

        if (config.SendAllMaxDegreeOfParallelism is { } sendAllLimit)
            services.AddSingleton(new ValiMediatorOptions { SendAllMaxDegreeOfParallelism = sendAllLimit });

        // Includes registrations from earlier AddValiMediator calls on the same collection.
        var scanned = new Dictionary<(Type Service, Type Implementation), ServiceDescriptor>();
        foreach (var descriptor in services)
            if (descriptor.ImplementationType is not null)
                scanned[(descriptor.ServiceType, descriptor.ImplementationType)] = descriptor;

        foreach (var (assembly, lifetime) in config.GetAssemblies())
        {
            var types = GetLoadableTypes(assembly)
                .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
                .ToList();

            foreach (var openInterface in ScannedInterfaces)
                RegisterImplementations(services, scanned, types, openInterface, lifetime);
        }

        RegisterExplicit(services, config.GetBehaviors());
        RegisterExplicit(services, config.GetPreProcessors());
        RegisterExplicit(services, config.GetPostProcessors());
        RegisterExplicit(services, config.GetRequestPreProcessors());
        RegisterExplicit(services, config.GetRequestPostProcessors());

        return services;
    }

    private static readonly Type[] ScannedInterfaces =
    {
        typeof(IRequestHandler<,>),
        typeof(INotificationHandler<>),
        typeof(IFireAndForgetHandler<>),
        typeof(IStreamRequestHandler<,>),
        typeof(IPreProcessor<>),
        typeof(IPreProcessor<,>),
        typeof(IPostProcessor<>),
        typeof(IPostProcessor<,>),
    };

    // Registers every closed form of openInterface a class implements. An exact (service, implementation)
    // pair scanned again is never duplicated: the last lifetime wins.
    private static void RegisterImplementations(
        IServiceCollection services,
        Dictionary<(Type Service, Type Implementation), ServiceDescriptor> scanned,
        List<Type> types,
        Type openInterface,
        ServiceLifetime lifetime)
    {
        foreach (var implementationType in types)
        foreach (var interfaceType in implementationType.GetInterfaces()
                     .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface))
        {
            if (scanned.TryGetValue((interfaceType, implementationType), out var existing))
            {
                if (existing.Lifetime == lifetime) continue;
                services.Remove(existing);
            }

            var created = ServiceDescriptor.Describe(interfaceType, implementationType, lifetime);
            services.Add(created);
            scanned[(interfaceType, implementationType)] = created;
        }
    }

    private static void RegisterExplicit(
        IServiceCollection services,
        IReadOnlyList<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> registrations)
    {
        foreach (var (serviceType, implementationType, lifetime) in registrations)
        {
            // An explicit registration must not duplicate the same pair already added by the assembly scan.
            if (services.Any(d => d.ServiceType == serviceType && d.ImplementationType == implementationType))
                continue;

            services.Add(ServiceDescriptor.Describe(serviceType, implementationType, lifetime));
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
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
}
