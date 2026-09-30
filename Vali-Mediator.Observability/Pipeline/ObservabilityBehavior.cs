using System.Diagnostics;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Observability.Core.Abstractions;
using Vali_Mediator_Observability.Core.Context;
using Vali_Mediator_Observability.Core.Diagnostics;
using Vali_Mediator_Observability.Core.Metrics;
using Vali_Mediator_Observability.Core.Options;

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
/// All registered observers are always invoked even if one throws; observer exceptions are isolated (recorded as an <c>observer.error</c> activity event) and never affect the request result or the original handler exception.
/// Register via <c>config.AddObservabilityBehavior()</c>.
/// </remarks>
/// <typeparam name="TRequest">The request type, must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class ObservabilityBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IRequestObserver> _observers;
    private readonly IMetricsCollector _metrics;
    private readonly ObservabilityOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="ObservabilityBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <param name="observers">All registered <see cref="IRequestObserver"/> instances.</param>
    /// <param name="metrics">The active <see cref="IMetricsCollector"/>.</param>
    /// <param name="options">Telemetry exposure options; <see cref="ObservabilityOptions"/> defaults when <c>null</c>.</param>
    public ObservabilityBehavior(
        IEnumerable<IRequestObserver> observers,
        IMetricsCollector metrics,
        ObservabilityOptions? options = null)
    {
        _observers = observers;
        _metrics = metrics;
        _options = options ?? new ObservabilityOptions();
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        Func<CancellationToken, Task<TResponse>> next,
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
        await InvokeObservers(o => o.OnStarted(context, cancellationToken), "OnStarted", activity).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            context.Duration = stopwatch.Elapsed;
            context.IsSuccess = true;
            context.Response = response;

            activity?.SetTag("request.success", true);
            activity?.SetTag("request.duration_ms", stopwatch.Elapsed.TotalMilliseconds);

            _metrics.RecordRequestCompleted(requestName, stopwatch.Elapsed, success: true);
            await InvokeObservers(o => o.OnCompleted(context, cancellationToken), "OnCompleted", activity).ConfigureAwait(false);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            context.Duration = stopwatch.Elapsed;
            context.IsSuccess = false;
            context.Exception = ex;

            activity?.SetStatus(ActivityStatusCode.Error, _options.Describe(ex));
            activity?.SetTag("request.success", false);
            activity?.SetTag("request.duration_ms", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetTag("exception.type", ex.GetType().FullName);

            _metrics.RecordRequestFailed(requestName, stopwatch.Elapsed, ex.GetType().FullName ?? ex.GetType().Name);
            await InvokeObservers(o => o.OnFailed(context, cancellationToken), "OnFailed", activity).ConfigureAwait(false);

            throw;
        }
    }

    // Observer failures are isolated: they never alter the request outcome. They are recorded on the activity.
    private async Task InvokeObservers(
        Func<IRequestObserver, Task> call, string hook, Activity? activity)
    {
        foreach (var observer in _observers)
        {
            try
            {
                await call(observer).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReportObserverError(observer, hook, ex, activity);
            }
        }
    }

    private void ReportObserverError(IRequestObserver observer, string hook, Exception ex, Activity? activity)
    {
        var observerType = observer.GetType().FullName ?? observer.GetType().Name;

        var tags = new ActivityTagsCollection
        {
            { "observer.type", observerType },
            { "observer.hook", hook },
            { "exception.type", ex.GetType().FullName }
        };
        if (_options.IncludeExceptionMessage)
            tags.Add("exception.message", ex.Message);
        activity?.AddEvent(new ActivityEvent("observer.error", tags: tags));

        try
        {
            _metrics.RecordObserverError(observerType, hook, ex);
        }
        catch
        {
            // A faulty collector must not turn an isolated observer error into a request failure.
        }
    }
}
