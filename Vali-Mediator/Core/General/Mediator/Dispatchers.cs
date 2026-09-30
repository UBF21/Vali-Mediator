using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Processors;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Streaming;

namespace Vali_Mediator.Core.General.Mediator;

// Typed dispatchers are created once per message type (one MakeGenericType per type, not per call),
// so the hot path resolves services and invokes handlers without reflection or object[] allocations.

internal static class ServiceResolution
{
    // MS.DI returns an array, which is an IReadOnlyList: no copy and no allocation when nothing is registered.
    internal static IReadOnlyList<T> All<T>(IServiceProvider serviceProvider)
    {
        var services = serviceProvider.GetServices<T>();
        return services as IReadOnlyList<T> ?? services.ToList();
    }
}

internal abstract class RequestDispatcher<TResponse>
{
    private static readonly ConcurrentDictionary<Type, RequestDispatcher<TResponse>> Cache = new();

    internal static RequestDispatcher<TResponse> For(Type requestType)
        => Cache.GetOrAdd(requestType, static type =>
            (RequestDispatcher<TResponse>)Activator.CreateInstance(
                typeof(RequestDispatcher<,>).MakeGenericType(type, typeof(TResponse)))!);

    /// <summary>Runs the pipeline, or returns null when no handler is registered.</summary>
    internal abstract Task<TResponse>? SendOrNull(
        IServiceProvider serviceProvider, IRequest<TResponse> request, CancellationToken cancellationToken);
}

internal sealed class RequestDispatcher<TRequest, TResponse> : RequestDispatcher<TResponse>
    where TRequest : IRequest<TResponse>
{
    internal override Task<TResponse>? SendOrNull(
        IServiceProvider serviceProvider, IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();
        return handler is null ? null : Execute(serviceProvider, handler, (TRequest)request, cancellationToken);
    }

    private static async Task<TResponse> Execute(
        IServiceProvider serviceProvider,
        IRequestHandler<TRequest, TResponse> handler,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var preProcessors = ServiceResolution.All<IPreProcessor<TRequest, TResponse>>(serviceProvider);
        var behaviors = ServiceResolution.All<IPipelineBehavior<TRequest, TResponse>>(serviceProvider);
        var postProcessors = ServiceResolution.All<IPostProcessor<TRequest, TResponse>>(serviceProvider);

        for (var i = 0; i < preProcessors.Count; i++)
            await preProcessors[i].Process(request, cancellationToken).ConfigureAwait(false);

        Func<CancellationToken, Task<TResponse>> pipeline = ct => handler.Handle(request, ct);
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var next = pipeline;
            var behavior = behaviors[i];
            pipeline = ct => behavior.Handle(request, next, ct);
        }

        var response = await pipeline(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < postProcessors.Count; i++)
            await postProcessors[i].Process(request, response, cancellationToken).ConfigureAwait(false);

        return response;
    }
}

internal abstract class FireAndForgetDispatcher
{
    private static readonly ConcurrentDictionary<Type, FireAndForgetDispatcher> Cache = new();

    internal static FireAndForgetDispatcher For(Type commandType)
        => Cache.GetOrAdd(commandType, static type =>
            (FireAndForgetDispatcher)Activator.CreateInstance(
                typeof(FireAndForgetDispatcher<>).MakeGenericType(type))!);

    internal abstract Task? SendOrNull(
        IServiceProvider serviceProvider, IFireAndForget command, CancellationToken cancellationToken);
}

internal sealed class FireAndForgetDispatcher<TCommand> : FireAndForgetDispatcher
    where TCommand : IFireAndForget
{
    internal override Task? SendOrNull(
        IServiceProvider serviceProvider, IFireAndForget command, CancellationToken cancellationToken)
    {
        var handler = serviceProvider.GetService<IFireAndForgetHandler<TCommand>>();
        return handler is null ? null : Execute(serviceProvider, handler, (TCommand)command, cancellationToken);
    }

    private static async Task Execute(
        IServiceProvider serviceProvider,
        IFireAndForgetHandler<TCommand> handler,
        TCommand command,
        CancellationToken cancellationToken)
    {
        var preProcessors = ServiceResolution.All<IPreProcessor<TCommand>>(serviceProvider);
        var behaviors = ServiceResolution.All<IPipelineBehavior<TCommand>>(serviceProvider);
        var postProcessors = ServiceResolution.All<IPostProcessor<TCommand>>(serviceProvider);

        for (var i = 0; i < preProcessors.Count; i++)
            await preProcessors[i].Process(command, cancellationToken).ConfigureAwait(false);

        Func<CancellationToken, Task> pipeline = ct => handler.Handle(command, ct);
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var next = pipeline;
            var behavior = behaviors[i];
            pipeline = ct => behavior.Handle(command, next, ct);
        }

        await pipeline(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < postProcessors.Count; i++)
            await postProcessors[i].Process(command, cancellationToken).ConfigureAwait(false);
    }
}

internal abstract class StreamDispatcher<TResponse>
{
    private static readonly ConcurrentDictionary<Type, StreamDispatcher<TResponse>> Cache = new();

    internal static StreamDispatcher<TResponse> For(Type requestType)
        => Cache.GetOrAdd(requestType, static type =>
            (StreamDispatcher<TResponse>)Activator.CreateInstance(
                typeof(StreamDispatcher<,>).MakeGenericType(type, typeof(TResponse)))!);

    internal abstract IAsyncEnumerable<TResponse>? CreateOrNull(
        IServiceProvider serviceProvider, IStreamRequest<TResponse> request, CancellationToken cancellationToken);
}

internal sealed class StreamDispatcher<TRequest, TResponse> : StreamDispatcher<TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    internal override IAsyncEnumerable<TResponse>? CreateOrNull(
        IServiceProvider serviceProvider, IStreamRequest<TResponse> request, CancellationToken cancellationToken)
        => serviceProvider.GetService<IStreamRequestHandler<TRequest, TResponse>>()
            ?.Handle((TRequest)request, cancellationToken);
}

internal sealed class NotificationEntry
{
    internal NotificationEntry(object handler, int priority, int group, Func<CancellationToken, Task> run)
    {
        Handler = handler;
        Priority = priority;
        Group = group;
        Run = run;
    }

    internal object Handler { get; }
    internal int Priority { get; }
    internal int Group { get; }
    internal Func<CancellationToken, Task> Run { get; }
}

// Everything to execute for one Publish: processors and handlers gathered from the static type (group 0)
// and, when it differs, from the runtime type (group 1).
internal sealed class NotificationPlan
{
    internal List<Func<CancellationToken, Task>> Pre { get; } = new();
    internal List<NotificationEntry> Entries { get; } = new();
    internal List<Func<CancellationToken, Task>> Post { get; } = new();
}

internal static class NotificationDispatch
{
    private static readonly ConcurrentDictionary<Type, Action<IServiceProvider, INotification, NotificationPlan>>
        RuntimeCollectors = new();

    private static readonly MethodInfo CollectBridgeMethod =
        typeof(NotificationDispatch).GetMethod(nameof(CollectBridge), BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static void CollectRuntime(
        Type runtimeType, IServiceProvider serviceProvider, INotification notification, NotificationPlan plan)
        => RuntimeCollectors.GetOrAdd(runtimeType, static type =>
            (Action<IServiceProvider, INotification, NotificationPlan>)CollectBridgeMethod
                .MakeGenericMethod(type)
                .CreateDelegate(typeof(Action<IServiceProvider, INotification, NotificationPlan>)))(
            serviceProvider, notification, plan);

    private static void CollectBridge<TNotification>(
        IServiceProvider serviceProvider, INotification notification, NotificationPlan plan)
        where TNotification : INotification
        => Collect(serviceProvider, (TNotification)notification, plan, group: 1);

    internal static void Collect<TNotification>(
        IServiceProvider serviceProvider, TNotification notification, NotificationPlan plan, int group)
        where TNotification : INotification
    {
        var behaviors = ServiceResolution.All<IPipelineBehavior<TNotification>>(serviceProvider);

        foreach (var preProcessor in ServiceResolution.All<IPreProcessor<TNotification>>(serviceProvider))
        {
            var pre = preProcessor;
            plan.Pre.Add(ct => pre.Process(notification, ct));
        }

        foreach (var handler in ServiceResolution.All<INotificationHandler<TNotification>>(serviceProvider))
        {
            // A class registered for both the base and the runtime type must run once, not twice.
            if (group != 0 && plan.Entries.Any(e => e.Group == 0 && e.Handler.GetType() == handler.GetType()))
                continue;

            var h = handler;
            plan.Entries.Add(new NotificationEntry(
                h, h.Priority, group, ct => RunHandler(h, notification, behaviors, ct)));
        }

        foreach (var postProcessor in ServiceResolution.All<IPostProcessor<TNotification>>(serviceProvider))
        {
            var post = postProcessor;
            plan.Post.Add(ct => post.Process(notification, ct));
        }
    }

    private static Task RunHandler<TNotification>(
        INotificationHandler<TNotification> handler,
        TNotification notification,
        IReadOnlyList<IPipelineBehavior<TNotification>> behaviors,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        // Respect INotificationFilter: skip the handler silently when ShouldHandle returns false.
        if (handler is INotificationFilter<TNotification> filter && !filter.ShouldHandle(notification))
            return Task.CompletedTask;

        Func<CancellationToken, Task> pipeline = ct => handler.Handle(notification, ct);
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var next = pipeline;
            var behavior = behaviors[i];
            pipeline = ct => behavior.Handle(notification, next, ct);
        }

        return pipeline(cancellationToken);
    }
}
