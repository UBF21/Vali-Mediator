using System.Diagnostics;
using Vali_Mediator.Core.General.Base;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator_Observability.Core.Diagnostics;
using Vali_Mediator_Observability.Core.Metrics;

namespace Vali_Mediator_Observability.Pipeline;

/// <summary>
/// A Vali-Mediator dispatch pipeline behavior that provides observability for
/// <c>INotification</c> and <c>IFireAndForget</c> executions.
/// </summary>
/// <remarks>
/// For each dispatch this behavior:
/// <list type="bullet">
///   <item>Starts an <see cref="Activity"/> via <see cref="ValiMediatorDiagnostics.ActivitySource"/> (zero overhead when no listener is attached).</item>
///   <item>Records start, completion, and failure metrics via <see cref="IMetricsCollector"/>.</item>
/// </list>
/// Register via <c>config.AddObservabilityBehavior()</c>.
/// </remarks>
/// <typeparam name="TRequest">The dispatch type, must implement <see cref="IDispatch"/>.</typeparam>
public sealed class ObservabilityDispatchBehavior<TRequest> : IPipelineBehavior<TRequest>
    where TRequest : IDispatch
{
    private readonly IMetricsCollector _metrics;

    /// <summary>
    /// Initializes a new instance of <see cref="ObservabilityDispatchBehavior{TRequest}"/>.
    /// </summary>
    /// <param name="metrics">The active <see cref="IMetricsCollector"/>.</param>
    public ObservabilityDispatchBehavior(IMetricsCollector metrics)
    {
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async Task Handle(
        TRequest request,
        Func<Task> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        using var activity = ValiMediatorDiagnostics.StartActivity(requestName);
        activity?.SetTag("request.name", requestName);
        activity?.SetTag("dispatch.type", "notification_or_fire_and_forget");

        _metrics.RecordRequestStarted(requestName);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next().ConfigureAwait(false);
            stopwatch.Stop();

            activity?.SetTag("request.success", true);
            activity?.SetTag("request.duration_ms", stopwatch.Elapsed.TotalMilliseconds);

            _metrics.RecordRequestCompleted(requestName, stopwatch.Elapsed, success: true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("request.success", false);
            activity?.SetTag("request.duration_ms", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetTag("exception.type", ex.GetType().FullName);

            _metrics.RecordRequestFailed(requestName, stopwatch.Elapsed, ex.GetType().FullName ?? ex.GetType().Name);

            throw;
        }
    }
}
