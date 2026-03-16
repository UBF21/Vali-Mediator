using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Cache;
using Vali_Mediator.Core.General.Exceptions;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Processors;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Streaming;

namespace Vali_Mediator.Core.General.Mediator;

/// <summary>
/// Default implementation of <see cref="IValiMediator"/>.
/// Resolves handlers from DI, builds behavior pipelines, and invokes pre/post processors.
/// </summary>
public class ValiMediator : IValiMediator
{
    private readonly IServiceProvider _serviceProvider;

    public ValiMediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType)
                      ?? throw new HandlerNotFoundException(requestType);

        return ExecuteRequestPipeline(request, handler, requestType, handlerType, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TResponse[]> SendAll<TResponse>(
        IEnumerable<IRequest<TResponse>> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests is null) throw new ArgumentNullException(nameof(requests));
        var tasks = requests.Select(r => Send(r, cancellationToken));
        return Task.WhenAll(tasks);
    }

    /// <inheritdoc/>
    public async Task<TResponse?> SendOrDefault<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType);

        if (handler is null) return default;

        return await ExecuteRequestPipeline(request, handler, requestType, handlerType, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task Publish<TNotification>(TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
        => Publish(notification, PublishStrategy.Sequential, cancellationToken);

    /// <inheritdoc/>
    public async Task Publish<TNotification>(TNotification notification,
        PublishStrategy strategy,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (notification is null) throw new ArgumentNullException(nameof(notification));

        var handlers = _serviceProvider
            .GetServices<INotificationHandler<TNotification>>()
            .OrderByDescending(h => h.Priority)
            .ToList();

        var preProcessorType = typeof(IPreProcessor<>).MakeGenericType(typeof(TNotification));
        var preProcessors = _serviceProvider.GetServices(preProcessorType).ToList();

        var behaviorType = typeof(IPipelineBehavior<>).MakeGenericType(typeof(TNotification));
        var behaviors = _serviceProvider.GetServices(behaviorType).ToList();

        var postProcessorType = typeof(IPostProcessor<>).MakeGenericType(typeof(TNotification));
        var postProcessors = _serviceProvider.GetServices(postProcessorType).ToList();

        var preProcessorMethod = ReflectionCache.GetMethod(preProcessorType, "Process");
        foreach (var preProcessor in preProcessors)
            await ((Task)preProcessorMethod.Invoke(preProcessor, new object[] { notification, cancellationToken })!)
                .ConfigureAwait(false);

        if (strategy == PublishStrategy.Parallel)
        {
            await Task.WhenAll(handlers.Select(h =>
                    ExecuteNotificationHandler(h, notification, behaviors, cancellationToken)))
                .ConfigureAwait(false);
        }
        else if (strategy == PublishStrategy.ResilientParallel)
        {
            var dlq = _serviceProvider.GetService<IDeadLetterQueue>();

            var handlerResults = await Task.WhenAll(handlers.Select(async h =>
            {
                try
                {
                    await ExecuteNotificationHandler(h, notification, behaviors, cancellationToken)
                        .ConfigureAwait(false);
                    return (Handler: (object)h, Exception: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Handler: (object)h, Exception: ex);
                }
            })).ConfigureAwait(false);

            var failures = handlerResults.Where(r => r.Exception is not null).ToList();

            if (dlq != null && failures.Count > 0)
            {
                foreach (var failure in failures)
                {
                    var entry = new DeadLetterEntry
                    {
                        NotificationTypeName = typeof(TNotification).FullName ?? typeof(TNotification).Name,
                        HandlerTypeName = failure.Handler.GetType().FullName ?? failure.Handler.GetType().Name,
                        Exception = failure.Exception!,
                        FailedAt = DateTimeOffset.UtcNow,
                        Notification = notification
                    };
                    await dlq.EnqueueAsync(entry, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                var exceptions = failures.Select(r => r.Exception!).ToList();
                if (exceptions.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
                if (exceptions.Count > 1) throw new AggregateException("One or more notification handlers failed.", exceptions);
            }
        }
        else
        {
            foreach (var handler in handlers)
                await ExecuteNotificationHandler(handler, notification, behaviors, cancellationToken)
                    .ConfigureAwait(false);
        }

        var postProcessorMethod = ReflectionCache.GetMethod(postProcessorType, "Process");
        foreach (var postProcessor in postProcessors)
            await ((Task)postProcessorMethod.Invoke(postProcessor, new object[] { notification, cancellationToken })!)
                .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task Send(IFireAndForget fireAndForget, CancellationToken cancellationToken = default)
    {
        if (fireAndForget is null) throw new ArgumentNullException(nameof(fireAndForget));

        var commandType = fireAndForget.GetType();
        var handlerType = typeof(IFireAndForgetHandler<>).MakeGenericType(commandType);
        var handler = _serviceProvider.GetService(handlerType)
                      ?? throw new HandlerNotFoundException(commandType);

        return ExecuteFireAndForgetPipeline(fireAndForget, handler, commandType, handlerType, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var requestType = request.GetType();
        var handlerType = typeof(IStreamRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = _serviceProvider.GetService(handlerType)
                      ?? throw new HandlerNotFoundException(requestType);

        var handlerMethod = ReflectionCache.GetMethod(handlerType, "Handle");
        return (IAsyncEnumerable<TResponse>)handlerMethod.Invoke(handler, new object[] { request, cancellationToken })!;
    }

    // -------------------------------------------------------------------------
    // Private pipeline helpers
    // -------------------------------------------------------------------------

    private async Task<TResponse> ExecuteRequestPipeline<TResponse>(
        IRequest<TResponse> request,
        object handler,
        Type requestType,
        Type handlerType,
        CancellationToken cancellationToken)
    {
        var preProcessorType = typeof(IPreProcessor<,>).MakeGenericType(requestType, typeof(TResponse));
        var preProcessors = _serviceProvider.GetServices(preProcessorType).ToList();

        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var behaviors = _serviceProvider.GetServices(behaviorType).ToList();

        var postProcessorType = typeof(IPostProcessor<,>).MakeGenericType(requestType, typeof(TResponse));
        var postProcessors = _serviceProvider.GetServices(postProcessorType).ToList();

        var preProcessorMethod = ReflectionCache.GetMethod(preProcessorType, "Process");
        foreach (var preProcessor in preProcessors)
            await ((Task)preProcessorMethod.Invoke(preProcessor, new object[] { request, cancellationToken })!)
                .ConfigureAwait(false);

        var handlerMethod = ReflectionCache.GetMethod(handlerType, "Handle");
        var behaviorMethod = ReflectionCache.GetMethod(behaviorType, "Handle");

        Func<Task<TResponse>> pipeline = () =>
            (Task<TResponse>)handlerMethod.Invoke(handler, new object[] { request, cancellationToken })!;

        foreach (var behavior in Enumerable.Reverse(behaviors))
        {
            var next = pipeline;
            pipeline = () => (Task<TResponse>)behaviorMethod.Invoke(behavior, new object[] { request, next, cancellationToken })!;
        }

        var response = await pipeline().ConfigureAwait(false);

        var postProcessorMethod = ReflectionCache.GetMethod(postProcessorType, "Process");
        foreach (var postProcessor in postProcessors)
            await ((Task)postProcessorMethod.Invoke(postProcessor, new object?[] { request, response, cancellationToken })!)
                .ConfigureAwait(false);

        return response;
    }

    private static Task ExecuteNotificationHandler<TNotification>(
        INotificationHandler<TNotification> handler,
        TNotification notification,
        List<object?> behaviors,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        // Respect INotificationFilter — skip handler silently when ShouldHandle returns false
        if (handler is INotificationFilter<TNotification> filter && !filter.ShouldHandle(notification))
            return Task.CompletedTask;

        Func<Task> pipeline = () => handler.Handle(notification, cancellationToken);

        foreach (var behavior in Enumerable.Reverse(behaviors))
        {
            var next = pipeline;
            var typedBehavior = (IPipelineBehavior<TNotification>)behavior!;
            pipeline = () => typedBehavior.Handle(notification, next, cancellationToken);
        }

        return pipeline();
    }

    private async Task ExecuteFireAndForgetPipeline(
        IFireAndForget fireAndForget,
        object handler,
        Type commandType,
        Type handlerType,
        CancellationToken cancellationToken)
    {
        var preProcessorType = typeof(IPreProcessor<>).MakeGenericType(commandType);
        var preProcessors = _serviceProvider.GetServices(preProcessorType).ToList();

        var behaviorType = typeof(IPipelineBehavior<>).MakeGenericType(commandType);
        var behaviors = _serviceProvider.GetServices(behaviorType).ToList();

        var postProcessorType = typeof(IPostProcessor<>).MakeGenericType(commandType);
        var postProcessors = _serviceProvider.GetServices(postProcessorType).ToList();

        var preProcessorMethod = ReflectionCache.GetMethod(preProcessorType, "Process");
        foreach (var preProcessor in preProcessors)
            await ((Task)preProcessorMethod.Invoke(preProcessor, new object[] { fireAndForget, cancellationToken })!)
                .ConfigureAwait(false);

        var handlerMethod = ReflectionCache.GetMethod(handlerType, "Handle");
        var behaviorMethod = ReflectionCache.GetMethod(behaviorType, "Handle");

        Func<Task> pipeline = () =>
            (Task)handlerMethod.Invoke(handler, new object[] { fireAndForget, cancellationToken })!;

        foreach (var behavior in Enumerable.Reverse(behaviors))
        {
            var next = pipeline;
            pipeline = () => (Task)behaviorMethod.Invoke(behavior, new object[] { fireAndForget, next, cancellationToken })!;
        }

        await pipeline().ConfigureAwait(false);

        var postProcessorMethod = ReflectionCache.GetMethod(postProcessorType, "Process");
        foreach (var postProcessor in postProcessors)
            await ((Task)postProcessorMethod.Invoke(postProcessor, new object[] { fireAndForget, cancellationToken })!)
                .ConfigureAwait(false);
    }
}
