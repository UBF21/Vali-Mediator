using System.Diagnostics;
using Vali_Mediator.Core.General.Base;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator_Observability.Core.Diagnostics;
using Vali_Mediator_Observability.Core.Metrics;
using Vali_Mediator_Observability.Core.Options;

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
    private readonly ObservabilityOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="ObservabilityDispatchBehavior{TRequest}"/>.
    /// </summary>
    /// <param name="metrics">The active <see cref="IMetricsCollector"/>.</param>
    /// <param name="options">Telemetry exposure options; <see cref="ObservabilityOptions"/> defaults when <c>null</c>.</param>
    public ObservabilityDispatchBehavior(IMetricsCollector metrics, ObservabilityOptions? options = null)
    {
        _metrics = metrics;
        _options = options ?? new ObservabilityOptions();
    }

    /// <inheritdoc />
    public async Task Handle(
        TRequest request,
        Func<CancellationToken, Task> next,
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
            await next(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            activity?.SetTag("request.success", true);
            activity?.SetTag("request.duration_ms", stopwatch.Elapsed.TotalMilliseconds);

            _metrics.RecordRequestCompleted(requestName, stopwatch.Elapsed, success: true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            activity?.SetStatus(ActivityStatusCode.Error, _options.Describe(ex));
            activity?.SetTag("request.success", false);
            activity?.SetTag("request.duration_ms", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetTag("exception.type", ex.GetType().FullName);

            _metrics.RecordRequestFailed(requestName, stopwatch.Elapsed, ex.GetType().FullName ?? ex.GetType().Name);

            throw;
        }
    }
}
