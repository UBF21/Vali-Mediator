using System.Diagnostics;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Observability.Core.Abstractions;
using Vali_Mediator_Observability.Core.Context;
using Vali_Mediator_Observability.Core.Diagnostics;
using Vali_Mediator_Observability.Core.Metrics;

namespace Vali_Mediator_Observability.Pipeline;

/// <summary>
/// A Vali-Mediator pipeline behavior that provides observability for <c>IRequest&lt;TResponse&gt;</c> executions.
/// </summary>
/// <remarks>
/// For each request this behavior:
/// <list type="bullet">
///   <item>Creates an <see cref="ObservabilityContext"/> with a unique <c>OperationId</c>.</item>
///   <item>Starts an <see cref="Activity"/> via <see cref="ValiMediatorDiagnostics.ActivitySource"/> (zero overhead when no listener is attached).</item>
///   <item>Invokes all registered <see cref="IRequestObserver.OnStarted"/> hooks.</item>
///   <item>On success: records duration, sets <c>IsSuccess = true</c>, populates <c>Response</c>, calls <see cref="IRequestObserver.OnCompleted"/> and <see cref="IMetricsCollector.RecordRequestCompleted"/>.</item>
///   <item>On exception: records duration, sets <c>IsSuccess = false</c>, populates <c>Exception</c>, calls <see cref="IRequestObserver.OnFailed"/> and <see cref="IMetricsCollector.RecordRequestFailed"/>.</item>
/// </list>
/// All registered observers are always invoked even if one throws; exceptions from observers are collected into an <see cref="AggregateException"/>.
/// Register via <c>config.AddObservabilityBehavior()</c>.
/// </remarks>
/// <typeparam name="TRequest">The request type, must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class ObservabilityBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IRequestObserver> _observers;
    private readonly IMetricsCollector _metrics;

    /// <summary>
    /// Initializes a new instance of <see cref="ObservabilityBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <param name="observers">All registered <see cref="IRequestObserver"/> instances.</param>
    /// <param name="metrics">The active <see cref="IMetricsCollector"/>.</param>
    public ObservabilityBehavior(IEnumerable<IRequestObserver> observers, IMetricsCollector metrics)
    {
        _observers = observers;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var context = new ObservabilityContext
        {
            RequestName = requestName,
            OperationId = Guid.NewGuid().ToString(),
            StartedAt = DateTimeOffset.UtcNow,
            Request = request
        };

        using var activity = ValiMediatorDiagnostics.StartActivity(requestName);
        activity?.SetTag("request.name", requestName);
        activity?.SetTag("operation.id", context.OperationId);

        _metrics.RecordRequestStarted(requestName);
        await InvokeObserversOnStarted(context, cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next().ConfigureAwait(false);
            stopwatch.Stop();

            context.Duration = stopwatch.Elapsed;
            context.IsSuccess = true;
            context.Response = response;

            activity?.SetTag("request.success", true);
            activity?.SetTag("request.duration_ms", stopwatch.Elapsed.TotalMilliseconds);

            _metrics.RecordRequestCompleted(requestName, stopwatch.Elapsed, success: true);
            await InvokeObserversOnCompleted(context, cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            context.Duration = stopwatch.Elapsed;
            context.IsSuccess = false;
            context.Exception = ex;

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("request.success", false);
            activity?.SetTag("request.duration_ms", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetTag("exception.type", ex.GetType().FullName);

            _metrics.RecordRequestFailed(requestName, stopwatch.Elapsed, ex.GetType().FullName ?? ex.GetType().Name);
            await InvokeObserversOnFailed(context, cancellationToken).ConfigureAwait(false);

            throw;
        }
    }

    private async Task InvokeObserversOnStarted(ObservabilityContext context, CancellationToken ct)
    {
        var exceptions = new List<Exception>();
        foreach (var observer in _observers)
        {
            try
            {
                await observer.OnStarted(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }
        if (exceptions.Count > 0)
            throw new AggregateException("One or more observers threw during OnStarted.", exceptions);
    }

    private async Task InvokeObserversOnCompleted(ObservabilityContext context, CancellationToken ct)
    {
        var exceptions = new List<Exception>();
        foreach (var observer in _observers)
        {
            try
            {
                await observer.OnCompleted(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }
        if (exceptions.Count > 0)
            throw new AggregateException("One or more observers threw during OnCompleted.", exceptions);
    }

    private async Task InvokeObserversOnFailed(ObservabilityContext context, CancellationToken ct)
    {
        var exceptions = new List<Exception>();
        foreach (var observer in _observers)
        {
            try
            {
                await observer.OnFailed(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }
        if (exceptions.Count > 0)
            throw new AggregateException("One or more observers threw during OnFailed.", exceptions);
    }
}
