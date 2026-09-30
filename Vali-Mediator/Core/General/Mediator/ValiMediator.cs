using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Exceptions;
using Vali_Mediator.Core.Notification;
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

    /// <summary>Initializes a new instance using the application's <see cref="IServiceProvider"/>.</summary>
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
        return RequestDispatcher<TResponse>.For(requestType).SendOrNull(_serviceProvider, request, cancellationToken)
               ?? throw new HandlerNotFoundException(requestType);
    }

    /// <inheritdoc/>
    public Task<TResponse[]> SendAll<TResponse>(
        IEnumerable<IRequest<TResponse>> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests is null) throw new ArgumentNullException(nameof(requests));

        var limit = _serviceProvider.GetService<ValiMediatorOptions>()?.SendAllMaxDegreeOfParallelism;
        if (limit is { } max)
            return ((IValiMediator)this).SendAll(requests, max, cancellationToken);

        var tasks = requests.Select(r => Send(r, cancellationToken));
        return Task.WhenAll(tasks);
    }

    /// <inheritdoc/>
    public async Task<TResponse?> SendOrDefault<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var pipeline = RequestDispatcher<TResponse>.For(request.GetType())
            .SendOrNull(_serviceProvider, request, cancellationToken);

        return pipeline is null ? default : await pipeline.ConfigureAwait(false);
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

        var plan = new NotificationPlan();
        NotificationDispatch.Collect(_serviceProvider, notification, plan, group: 0);

        // Handlers registered for the runtime type run too (MS.DI does not resolve by variance).
        var runtimeType = notification.GetType();
        if (runtimeType != typeof(TNotification))
            NotificationDispatch.CollectRuntime(runtimeType, _serviceProvider, notification, plan);

        foreach (var pre in plan.Pre)
            await pre(cancellationToken).ConfigureAwait(false);

        var entries = plan.Entries.OrderByDescending(e => e.Priority).ToList();
        switch (strategy)
        {
            case PublishStrategy.Parallel:
                await Task.WhenAll(entries.Select(e => e.Run(cancellationToken))).ConfigureAwait(false);
                break;
            case PublishStrategy.ResilientParallel:
                await PublishResilientParallel(entries, notification, cancellationToken).ConfigureAwait(false);
                break;
            default:
                foreach (var entry in entries)
                    await entry.Run(cancellationToken).ConfigureAwait(false);
                break;
        }

        foreach (var post in plan.Post)
            await post(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task Send(IFireAndForget fireAndForget, CancellationToken cancellationToken = default)
    {
        if (fireAndForget is null) throw new ArgumentNullException(nameof(fireAndForget));

        var commandType = fireAndForget.GetType();
        return FireAndForgetDispatcher.For(commandType).SendOrNull(_serviceProvider, fireAndForget, cancellationToken)
               ?? throw new HandlerNotFoundException(commandType);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var requestType = request.GetType();
        return StreamDispatcher<TResponse>.For(requestType).CreateOrNull(_serviceProvider, request, cancellationToken)
               ?? throw new HandlerNotFoundException(requestType);
    }

    private async Task PublishResilientParallel(
        List<NotificationEntry> entries,
        INotification notification,
        CancellationToken cancellationToken)
    {
        var dlq = _serviceProvider.GetService<IDeadLetterQueue>();

        var handlerResults = await Task.WhenAll(entries.Select(async entry =>
        {
            try
            {
                await entry.Run(cancellationToken).ConfigureAwait(false);
                return (entry.Handler, Exception: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (entry.Handler, Exception: ex);
            }
        })).ConfigureAwait(false);

        var failures = handlerResults.Where(r => r.Exception is not null).Select(r => (r.Handler, Exception: r.Exception!)).ToList();

        if (dlq != null && failures.Count > 0)
        {
            foreach (var (handler, exception) in failures)
                await dlq.EnqueueAsync(CreateDeadLetter(handler, exception, notification), cancellationToken)
                    .ConfigureAwait(false);
            return;
        }

        var exceptions = failures.Select(f => f.Exception).ToList();
        if (exceptions.Count == 1) ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        if (exceptions.Count > 1) throw new AggregateException("One or more notification handlers failed.", exceptions);
    }

    private static DeadLetterEntry CreateDeadLetter(object handler, Exception exception, INotification notification)
    {
        var notificationType = notification.GetType();
        var handlerType = handler.GetType();
        return new DeadLetterEntry
        {
            NotificationTypeName = notificationType.FullName ?? notificationType.Name,
            HandlerTypeName = handlerType.FullName ?? handlerType.Name,
            Exception = exception,
            FailedAt = DateTimeOffset.UtcNow,
            Notification = notification
        };
    }
}
