using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Processors;
using Vali_Mediator.Core.Request;

namespace Vali_Mediator.Core.General.Mediator;

/// <summary>
/// Implements the Mediator pattern for handling requests and publishing notifications using dependency injection.
/// </summary>
public class ValiMediator : IValiMediator
{
    private readonly IServiceProvider _serviceProvider;

    public ValiMediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType) 
                      ?? throw new InvalidOperationException($"No handler found for the request {requestType.Name}");

        // Obtener preprocesadores
        var preProcessorType = typeof(IPreProcessor<,>).MakeGenericType(requestType, typeof(TResponse));
        var preProcessors = _serviceProvider.GetServices(preProcessorType).ToList();
        
        // Obtener comportamientos
        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToList();
        
        // Obtener postprocesadores
        var postProcessorType = typeof(IPostProcessor<,>).MakeGenericType(requestType, typeof(TResponse));
        var postProcessors = _serviceProvider.GetServices(postProcessorType).ToList();

        // Ejecutar preprocesadores
        foreach (var preProcessor in preProcessors)
        {
            var preProcessorMethod = preProcessorType.GetMethod("Process");
            preProcessorMethod?.Invoke(preProcessor, new object[] { request, cancellationToken });
        }
        
        // Función que ejecuta el manejador
        Func<Task<TResponse>> handlerDelegate = async () =>
        {
            var method = handlerType.GetMethod("Handle");
            return await (Task<TResponse>)method?.Invoke(handler, new object[] { request, cancellationToken })!;
        };

        // Construir el pipeline
        Func<Task<TResponse>> next = handlerDelegate;
        foreach (var behavior in behaviors)
        {
            var currentNext = next;
            var behaviorGenericType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
            var behaviorMethod = behaviorGenericType.GetMethod("Handle");
            next = () => (Task<TResponse>)behaviorMethod?.Invoke(behavior, new object[] { request, currentNext, cancellationToken })!;
        }

        var response = await next();
        
        // Ejecutar postprocesadores
        foreach (var postProcessor in postProcessors)
        {
            var postProcessorMethod = postProcessorType.GetMethod("Process");
            if (response != null) postProcessorMethod?.Invoke(postProcessor, new object[] { request, response, cancellationToken });
        }   
        
        return response;
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (notification == null) throw new ArgumentNullException(nameof(notification));

        var notificationType = typeof(INotificationHandler<>).MakeGenericType(typeof(TNotification));
        var handlers = _serviceProvider.GetServices(notificationType)
            .Cast<INotificationHandler<TNotification>>()
            .OrderByDescending(h => h.Priority);
        
        // Obtener preprocesadores
        var preProcessorType = typeof(IPreProcessor<>).MakeGenericType(typeof(TNotification));
        var preProcessors = _serviceProvider.GetServices(preProcessorType).ToList();

        // Obtener comportamientos
        var behaviorType = typeof(IPipelineBehavior<>).MakeGenericType(typeof(TNotification));
        var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToList();
        
        // Obtener postprocesadores
        var postProcessorType = typeof(IPostProcessor<>).MakeGenericType(typeof(TNotification));
        var postProcessors = _serviceProvider.GetServices(postProcessorType).ToList();

        // Ejecutar preprocesadores
        foreach (var preProcessor in preProcessors)
        {
            var preProcessorMethod = preProcessorType.GetMethod("Process");
            preProcessorMethod?.Invoke(preProcessor, new object[] { notification, cancellationToken });
        }
        
        foreach (var handler in handlers)
        {
            var handlerMethod = handler.GetType().GetMethod("Handle");
            if (handlerMethod == null)
            {
                throw new InvalidOperationException($"The handler {handler.GetType().Name} does not have a 'Handle' method.");
            }

            async Task HandlerDelegate() =>
                await (Task)handlerMethod.Invoke(handler, new object[] { notification, cancellationToken })!;

            Func<Task> next = HandlerDelegate;
            foreach (var behavior in behaviors)
            {
                var currentNext = next;
                var typedBehavior = (IPipelineBehavior<TNotification>)behavior!;
                next = () => typedBehavior.Handle(notification, currentNext, cancellationToken);
            }

            await next();
        }
        
        // Ejecutar postprocesadores
        foreach (var postProcessor in postProcessors)
        {
            var postProcessorMethod = postProcessorType.GetMethod("Process");
            postProcessorMethod?.Invoke(postProcessor, new object[] { notification, cancellationToken });
        }
    }

    public async Task Send(IFireAndForget fireAndForget, CancellationToken cancellationToken = default)
    {
        if (fireAndForget == null) throw new ArgumentNullException(nameof(fireAndForget));

        var commandType = fireAndForget.GetType();
        var handlerType = typeof(IFireAndForgetHandler<>).MakeGenericType(commandType);
        var handler = _serviceProvider.GetService(handlerType) ??
                      throw new InvalidOperationException($"No handler found for the command {commandType.Name}");

        // Obtener preprocesadores
        var preProcessorType = typeof(IPreProcessor<>).MakeGenericType(commandType);
        var preProcessors = _serviceProvider.GetServices(preProcessorType).ToList();
        
        // Usar IPipelineBehavior<> con un solo parámetro genérico
        var behaviorType = typeof(IPipelineBehavior<>).MakeGenericType(commandType);
        var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToList();

        // Obtener postprocesadores
        var postProcessorType = typeof(IPostProcessor<>).MakeGenericType(commandType);
        var postProcessors = _serviceProvider.GetServices(postProcessorType).ToList();
        
        // Ejecutar preprocesadores
        foreach (var preProcessor in preProcessors)
        {
            var preProcessorMethod = preProcessorType.GetMethod("Process");
            preProcessorMethod?.Invoke(preProcessor, new object[] { fireAndForget, cancellationToken });
        }
        
        var handlerMethod = handlerType.GetMethod("Handle");
        if (handlerMethod == null)
        {
            throw new InvalidOperationException($"The handler {handlerType.Name} does not have a 'Handle' method.");
        }

        async Task HandlerDelegate() =>
            await (Task)handlerMethod.Invoke(handler, new object[] { fireAndForget, cancellationToken })!;

        Func<Task> next = HandlerDelegate;
        foreach (var behavior in behaviors)
        {
            var currentNext = next;
            var behaviorGenericType = typeof(IPipelineBehavior<>).MakeGenericType(commandType);
            var behaviorMethod = behaviorGenericType.GetMethod("Handle");
            if (behaviorMethod == null)
            {
                throw new InvalidOperationException(
                    $"The behavior {behavior?.GetType().Name} does not have a 'Handle' method.");
            }

            next = () => (Task)behaviorMethod.Invoke(behavior,
                new object[] { fireAndForget, currentNext, cancellationToken })!;
        }

        await next();
        
        // Ejecutar postprocesadores
        foreach (var postProcessor in postProcessors)
        {
            var postProcessorMethod = postProcessorType.GetMethod("Process");
            postProcessorMethod?.Invoke(postProcessor, new object[] { fireAndForget, cancellationToken });
        }
    }
}