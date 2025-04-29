using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Base;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Processors;
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

        List<Assembly> assemblies = config.GetAssemblies().ToList();

        if (assemblies.Any())
        {
            IEnumerable<Type> handlerTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces()
                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)));

            foreach (Type handlerType in handlerTypes)
            {
                Type interfaceType = handlerType.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));
                services.AddScoped(interfaceType, handlerType);
            }

            // Registrar INotificationHandler
            IEnumerable<Type> notificationHandlerTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces()
                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)));

            foreach (Type handlerType in notificationHandlerTypes)
            {
                Type interfaceType = handlerType.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>));
                services.AddScoped(interfaceType, handlerType);
            }

            IEnumerable<Type> fireAndForgetHandlerTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces()
                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IFireAndForgetHandler<>)));

            foreach (Type handlerType in fireAndForgetHandlerTypes)
            {
                Type interfaceType = handlerType.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IFireAndForgetHandler<>));
                services.AddScoped(interfaceType, handlerType);
            }
        }

        // Registrar solo comportamientos explícitos
        foreach (KeyValuePair<Type,Type> behavior in config.GetBehaviors())
        {
            if (behavior.Key.IsGenericType && behavior.Key.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))
            {
                services.AddScoped(behavior.Key, behavior.Value);
            }
            else if (behavior.Key.IsGenericType &&
                     behavior.Key.GetGenericTypeDefinition() == typeof(IPipelineBehavior<>))
            {
                // Asegurarse de que el comportamiento se aplique a tipos específicos como LogProductCommand
                IEnumerable<Type> requestTypes = assemblies
                    .SelectMany(a => a.GetTypes())
                    .Where(t => typeof(IDispatch).IsAssignableFrom(t) &&
                                t is { IsInterface: false, IsAbstract: false });

                foreach (Type requestType in requestTypes)
                {
                    Type specificBehaviorType = behavior.Value.MakeGenericType(requestType);
                    Type specificInterfaceType = typeof(IPipelineBehavior<>).MakeGenericType(requestType);
                    services.AddScoped(specificInterfaceType, specificBehaviorType);
                }
            }
            else
            {
                throw new ArgumentException($"Unsupported behavior type: {behavior.Key}");
            }
        }

        // Registrar preprocesadores para IDispatch (INotification, IFireAndForget)
        foreach (KeyValuePair<Type,Type> preProcessor in config.GetPreProcessors())
        {
            IEnumerable<Type> requestTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IDispatch).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

            foreach (Type requestType in requestTypes)
            {
                Type specificInterfaceType = typeof(IPreProcessor<>).MakeGenericType(requestType);
                if (preProcessor.Key.GetGenericArguments().Length == 1 &&
                    preProcessor.Key.GetGenericArguments()[0] == requestType)
                {
                    services.AddScoped(specificInterfaceType, preProcessor.Value);
                }
            }
        }

        // Registrar postprocesadores para IDispatch (INotification, IFireAndForget)
        foreach (KeyValuePair<Type,Type> postProcessor in config.GetPostProcessors())
        {
            IEnumerable<Type> requestTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IDispatch).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

            foreach (Type requestType in requestTypes)
            {
                Type specificInterfaceType = typeof(IPostProcessor<>).MakeGenericType(requestType);
                if (postProcessor.Key.GetGenericArguments().Length == 1 &&
                    postProcessor.Key.GetGenericArguments()[0] == requestType)
                {
                    services.AddScoped(specificInterfaceType, postProcessor.Value);
                }
            }
        }

        // Registrar preprocesadores para IRequest<TResponse>
        foreach (KeyValuePair<Type,Type> preProcessor in config.GetRequestPreProcessors())
        {
            IEnumerable<Type> requestTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t.GetInterfaces()
                                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)) &&
                            t is { IsInterface: false, IsAbstract: false });

            foreach (Type requestType in requestTypes)
            {
                Type requestInterface = requestType.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));
                Type responseType = requestInterface.GetGenericArguments()[0];
                Type specificInterfaceType = typeof(IPreProcessor<,>).MakeGenericType(requestType, responseType);
                if (preProcessor.Key.GetGenericArguments().Length == 2 &&
                    preProcessor.Key.GetGenericArguments()[0] == requestType &&
                    preProcessor.Key.GetGenericArguments()[1] == responseType)
                {
                    services.AddScoped(specificInterfaceType, preProcessor.Value);
                }
            }
        }

        // Registrar postprocesadores para IRequest
        foreach (KeyValuePair<Type, Type> postProcessor in config.GetRequestPostProcessors())
        {
            IEnumerable<Type> requestTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t.GetInterfaces()
                                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)) &&
                            t is { IsInterface: false, IsAbstract: false });

            foreach (Type requestType in requestTypes)
            {
                Type requestInterface = requestType.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));
                Type responseType = requestInterface.GetGenericArguments()[0];
                Type specificInterfaceType = typeof(IPostProcessor<,>).MakeGenericType(requestType, responseType);
                if (postProcessor.Key.GetGenericArguments().Length == 2 &&
                    postProcessor.Key.GetGenericArguments()[0] == requestType &&
                    postProcessor.Key.GetGenericArguments()[1] == responseType)
                {
                    services.AddScoped(specificInterfaceType, postProcessor.Value);
                }
            }
        }

        return services;
    }
}