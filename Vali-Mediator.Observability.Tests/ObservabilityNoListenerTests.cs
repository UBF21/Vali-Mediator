using Vali_Mediator_Observability.Core.Diagnostics;
using Vali_Mediator_Observability.Core.Metrics;
using Vali_Mediator_Observability.Pipeline;
using Xunit;

namespace Vali_Mediator_Observability.Tests;

// Any ActivityListener alive in the process makes StartActivity return a span. Tests that need the "no listener"
// (null Activity) path therefore share this collection with the listener-based tests, which xUnit runs serially and
// never concurrently with other collections.
[CollectionDefinition("Activity", DisableParallelization = true)]
public sealed class ActivityCollection { }

[Collection("Activity")]
public class ObservabilityNoListenerTests
{
    [Fact]
    public void StartActivity_WithoutListener_ReturnsNull()
    {
        Assert.Null(ValiMediatorDiagnostics.StartActivity("nobody-listens"));
    }

    [Fact]
    public async Task Dispatch_Success_WithoutListener_StillRecordsMetrics()
    {
        var metrics = new RecordingMetrics();
        var behavior = new ObservabilityDispatchBehavior<UserCreated>(metrics);

        await behavior.Handle(new UserCreated(), _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal((1, 1, 0), (metrics.Started, metrics.Completed, metrics.Failed));
    }

    [Fact]
    public async Task Dispatch_Failure_WithoutListener_RecordsFailureAndRethrows()
    {
        var metrics = new RecordingMetrics();
        var behavior = new ObservabilityDispatchBehavior<SendEmail>(metrics);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new SendEmail(), _ => throw new InvalidOperationException("smtp down"),
                CancellationToken.None));

        Assert.Equal("smtp down", ex.Message);
        Assert.Equal((1, 0, 1), (metrics.Started, metrics.Completed, metrics.Failed));
    }

    [Fact]
    public async Task Request_Success_WithoutListener_ReturnsTheResponse()
    {
        var metrics = new RecordingMetrics();
        var behavior = new ObservabilityBehavior<TelemetryRequest, string>(
            new Vali_Mediator_Observability.Core.Abstractions.IRequestObserver[0], metrics);

        var response = await behavior.Handle(new TelemetryRequest(), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", response);
        Assert.Equal((1, 1, 0), (metrics.Started, metrics.Completed, metrics.Failed));
    }

    [Fact]
    public async Task Request_Failure_WithoutListener_RecordsFailureAndRethrows()
    {
        var metrics = new RecordingMetrics();
        var behavior = new ObservabilityBehavior<TelemetryRequest, string>(
            new Vali_Mediator_Observability.Core.Abstractions.IRequestObserver[0], metrics);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            behavior.Handle(new TelemetryRequest(), _ => throw new TimeoutException(), CancellationToken.None));

        Assert.Equal((1, 0, 1), (metrics.Started, metrics.Completed, metrics.Failed));
    }

    [Fact]
    public async Task Request_CancelledHandler_IsRecordedAsFailureAndKeepsTheCancellation()
    {
        var metrics = new RecordingMetrics();
        var behavior = new ObservabilityBehavior<TelemetryRequest, string>(
            new Vali_Mediator_Observability.Core.Abstractions.IRequestObserver[0], metrics);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            behavior.Handle(new TelemetryRequest(),
                ct => Task.FromCanceled<string>(ct), cts.Token));

        Assert.Equal((1, 0, 1), (metrics.Started, metrics.Completed, metrics.Failed));
    }

    [Fact]
    public async Task Dispatch_CancelledHandler_IsRecordedAsFailureAndKeepsTheCancellation()
    {
        var metrics = new RecordingMetrics();
        var behavior = new ObservabilityDispatchBehavior<UserCreated>(metrics);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            behavior.Handle(new UserCreated(), ct => Task.FromCanceled(ct), cts.Token));

        Assert.Equal((1, 0, 1), (metrics.Started, metrics.Completed, metrics.Failed));
    }

    [Fact]
    public void NoOpMetricsCollector_AllHooksAreSilent()
    {
        IMetricsCollector collector = new NoOpMetricsCollector();

        var thrown = Record.Exception(() =>
        {
            collector.RecordRequestStarted("Req");
            collector.RecordRequestCompleted("Req", TimeSpan.FromMilliseconds(1), success: true);
            collector.RecordRequestFailed("Req", TimeSpan.FromMilliseconds(1), "System.Exception");
            collector.RecordObserverError("Obs", "OnStarted", new Exception("x"));
        });

        Assert.Null(thrown);
    }
}
