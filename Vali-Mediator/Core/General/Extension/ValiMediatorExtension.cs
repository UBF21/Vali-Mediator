using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;

namespace Vali_Mediator.Core.General.Extension;

/// <summary>
/// Provides extension methods for registering Vali-Mediator services with dependency injection.
/// </summary>
public static class ValiMediatorExtension
{
    /// <summary>
    /// Adds Vali-Mediator services to the specified service collection, configuring handlers and behaviors.
    /// </summary>
    /// <param name="services">The service collection to which Vali-Mediator services will be added.</param>
    /// <param name="configure">An action to configure the <see cref="ValiMediatorConfiguration"/> instance.</param>
    /// <returns>The modified <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    public static IServiceCollection AddValiMediator(
        this IServiceCollection services,
        Action<ValiMediatorConfiguration> configure)
    {
        var config = new ValiMediatorConfiguration();
        configure.Invoke(config);

        services.AddScoped<IValiMediator, ValiMediator>();

        var assemblies = config.GetAssemblies().ToList();
        if (assemblies.Any())
        {
            var handlerTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces()
                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)));

            foreach (var handlerType in handlerTypes)
            {
                var interfaceType = handlerType.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));
                services.AddScoped(interfaceType, handlerType);
            }

            var notificationHandlerTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces()
                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)));

            foreach (var handlerType in notificationHandlerTypes)
            {
                var interfaceType = handlerType.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>));
                services.AddScoped(interfaceType, handlerType);
            }
        }

        foreach (var behavior in config.GetBehaviors())
        {
            services.AddScoped(behavior.Key, behavior.Value);
        }

        return services;
    }
}