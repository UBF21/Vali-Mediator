using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Notification;
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
        Type requestType = request.GetType();
        Type handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        object handler = _serviceProvider.GetService(handlerType) ??
                         throw new InvalidOperationException($"No handler found for the request {requestType.Name}");

        Type behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        List<object?> behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToList();

        MethodInfo? handlerMethod = handlerType.GetMethod("Handle");
        if (handlerMethod == null)
        {
            throw new InvalidOperationException($"The handler {handlerType.Name} does not have a 'Handle' method.");
        }

        async Task<TResponse> HandlerDelegate() =>
            await (Task<TResponse>)handlerMethod.Invoke(handler, new object[] { request, cancellationToken })!;

        Func<Task<TResponse>> next = HandlerDelegate;
        foreach (object? behavior in behaviors)
        {
            var currentNext = next;
            var behaviorGenericType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
            var behaviorMethod = behaviorGenericType.GetMethod("Handle");
            if (behaviorMethod == null)
            {
                throw new InvalidOperationException(
                    $"The behavior {behavior?.GetType().Name} does not have a 'Handle' method.");
            }

            next = () =>
                (Task<TResponse>)behaviorMethod.Invoke(behavior,
                    new object[] { request, currentNext, cancellationToken })!;
        }

        return await next();
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        Type notificationType = typeof(INotificationHandler<>).MakeGenericType(typeof(TNotification));
        IEnumerable<INotificationHandler<TNotification>> handlers = _serviceProvider.GetServices(notificationType)
            .Cast<INotificationHandler<TNotification>>();

        foreach (INotificationHandler<TNotification>? handler in handlers)
        {
            await handler.Handle(notification, cancellationToken);
        }
    }
}