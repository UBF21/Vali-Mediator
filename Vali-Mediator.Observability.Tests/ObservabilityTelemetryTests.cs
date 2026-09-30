using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Observability.Core.Abstractions;
using Vali_Mediator_Observability.Core.Context;
using Vali_Mediator_Observability.Core.Diagnostics;
using Vali_Mediator_Observability.Core.Metrics;
using Vali_Mediator_Observability.Core.Options;
using Vali_Mediator_Observability.Extension;
using Vali_Mediator_Observability.Pipeline;
using Xunit;

namespace Vali_Mediator_Observability.Tests;

// Unique request names keep the process-wide ActivityListener from seeing spans of other test classes.
internal sealed class TelemetryRequest : IRequest<string> { }

internal sealed class TelemetryNotification : INotification { }

internal sealed class ObserverErrorMetrics : IMetricsCollector
{
    public List<(string Observer, string Hook, Exception Error)> ObserverErrors { get; } =
        new List<(string, string, Exception)>();

    public void RecordRequestStarted(string requestName) { }
    public void RecordRequestCompleted(string requestName, TimeSpan duration, bool success) { }
    public void RecordRequestFailed(string requestName, TimeSpan duration, string exceptionType) { }

    public void RecordObserverError(string observerType, string hook, Exception exception)
        => ObserverErrors.Add((observerType, hook, exception));
}

internal sealed class FaultyObserverErrorMetrics : IMetricsCollector
{
    public void RecordRequestStarted(string requestName) { }
    public void RecordRequestCompleted(string requestName, TimeSpan duration, bool success) { }
    public void RecordRequestFailed(string requestName, TimeSpan duration, string exceptionType) { }

    public void RecordObserverError(string observerType, string hook, Exception exception)
        => throw new InvalidOperationException("collector boom");
}

internal sealed class SecretThrowingObserver : IRequestObserver
{
    public Task OnStarted(ObservabilityContext context, CancellationToken ct = default)
        => throw new InvalidOperationException("password=hunter2");

    public Task OnCompleted(ObservabilityContext context, CancellationToken ct = default) => Task.CompletedTask;

    public Task OnFailed(ObservabilityContext context, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class ActivityCapture : IDisposable
{
    private readonly ActivityListener _listener;
    public List<Activity> Stopped { get; } = new List<Activity>();

    public ActivityCapture(string operationName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Vali-Mediator",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == operationName)
                    lock (Stopped) Stopped.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public Activity Single()
    {
        lock (Stopped) return Assert.Single(Stopped);
    }

    public void Dispose() => _listener.Dispose();
}

[Collection("Activity")]
public class ObservabilityTelemetryTests
{
    private static ObservabilityBehavior<TelemetryRequest, string> Behavior(
        IMetricsCollector metrics, ObservabilityOptions? options, params IRequestObserver[] observers)
        => new ObservabilityBehavior<TelemetryRequest, string>(observers, metrics, options);

    private static object? Tag(Activity activity, string key)
        => activity.TagObjects.FirstOrDefault(t => t.Key == key).Value;

    [Fact]
    public void ActivitySource_HasExpectedName()
    {
        Assert.Equal("Vali-Mediator", ValiMediatorDiagnostics.ActivitySource.Name);
    }

    [Fact]
    public async Task Success_CreatesSpanWithTags()
    {
        using var capture = new ActivityCapture(nameof(TelemetryRequest));
        var behavior = Behavior(new NoOpMetricsCollector(), null);

        await behavior.Handle(new TelemetryRequest(), _ => Task.FromResult("ok"), CancellationToken.None);

        var activity = capture.Single();
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal(nameof(TelemetryRequest), Tag(activity, "request.name"));
        Assert.NotNull(Tag(activity, "operation.id"));
        Assert.Equal(true, Tag(activity, "request.success"));
        Assert.NotNull(Tag(activity, "request.duration_ms"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task Failure_DefaultOptions_StatusHidesExceptionMessage()
    {
        using var capture = new ActivityCapture(nameof(TelemetryRequest));
        var behavior = Behavior(new NoOpMetricsCollector(), null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new TelemetryRequest(),
                _ => throw new InvalidOperationException("secret connection string"), CancellationToken.None));

        var activity = capture.Single();
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.StatusDescription);
        Assert.Equal(false, Tag(activity, "request.success"));
        Assert.Equal(typeof(InvalidOperationException).FullName, Tag(activity, "exception.type"));
    }

    [Fact]
    public async Task Failure_IncludeExceptionMessage_StatusContainsMessage()
    {
        using var capture = new ActivityCapture(nameof(TelemetryRequest));
        var behavior = Behavior(new NoOpMetricsCollector(), new ObservabilityOptions { IncludeExceptionMessage = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new TelemetryRequest(), _ => throw new InvalidOperationException("visible"),
                CancellationToken.None));

        Assert.Equal("visible", capture.Single().StatusDescription);
    }

    [Fact]
    public async Task ObserverError_DefaultOptions_EventHasTypeButNoMessage()
    {
        using var capture = new ActivityCapture(nameof(TelemetryRequest));
        var behavior = Behavior(new NoOpMetricsCollector(), null, new SecretThrowingObserver());

        await behavior.Handle(new TelemetryRequest(), _ => Task.FromResult("ok"), CancellationToken.None);

        var evt = Assert.Single(capture.Single().Events, e => e.Name == "observer.error");
        var tags = evt.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal(typeof(SecretThrowingObserver).FullName, tags["observer.type"]);
        Assert.Equal("OnStarted", tags["observer.hook"]);
        Assert.Equal(typeof(InvalidOperationException).FullName, tags["exception.type"]);
        Assert.False(tags.ContainsKey("exception.message"));
    }

    [Fact]
    public async Task ObserverError_IncludeExceptionMessage_EventHasMessage()
    {
        using var capture = new ActivityCapture(nameof(TelemetryRequest));
        var behavior = Behavior(new NoOpMetricsCollector(),
            new ObservabilityOptions { IncludeExceptionMessage = true }, new SecretThrowingObserver());

        await behavior.Handle(new TelemetryRequest(), _ => Task.FromResult("ok"), CancellationToken.None);

        var evt = Assert.Single(capture.Single().Events, e => e.Name == "observer.error");
        Assert.Equal("password=hunter2", evt.Tags.ToDictionary(t => t.Key, t => t.Value)["exception.message"]);
    }

    [Fact]
    public async Task ObserverError_WithoutActivityListener_IsStillReportedToMetrics()
    {
        var metrics = new ObserverErrorMetrics();
        var behavior = Behavior(metrics, null, new SecretThrowingObserver());

        var result = await behavior.Handle(new TelemetryRequest(), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        var error = Assert.Single(metrics.ObserverErrors);
        Assert.Equal(typeof(SecretThrowingObserver).FullName, error.Observer);
        Assert.Equal("OnStarted", error.Hook);
        Assert.IsType<InvalidOperationException>(error.Error);
    }

    [Fact]
    public async Task ObserverError_FaultyMetricsCollector_DoesNotAffectRequest()
    {
        var behavior = Behavior(new FaultyObserverErrorMetrics(), null, new SecretThrowingObserver());

        var result = await behavior.Handle(new TelemetryRequest(), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task ObserverError_FirstObserverThrows_SecondStillRunsAndEachFailureIsReported()
    {
        var metrics = new ObserverErrorMetrics();
        var tracking = new TrackingObserver();
        var behavior = Behavior(metrics, null, new ThrowingObserver(), tracking);

        await behavior.Handle(new TelemetryRequest(), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal(new[] { "started", "completed" }, tracking.Events);
        Assert.Equal(new[] { "OnStarted", "OnCompleted" }, metrics.ObserverErrors.Select(e => e.Hook));
    }

    [Fact]
    public async Task Dispatch_Failure_DefaultOptions_StatusHidesMessage()
    {
        using var capture = new ActivityCapture(nameof(TelemetryNotification));
        var behavior = new ObservabilityDispatchBehavior<TelemetryNotification>(new NoOpMetricsCollector());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new TelemetryNotification(), _ => throw new InvalidOperationException("secret"),
                CancellationToken.None));

        var activity = capture.Single();
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.StatusDescription);
        Assert.Equal(false, Tag(activity, "request.success"));
    }

    [Fact]
    public async Task Dispatch_Failure_IncludeExceptionMessage_StatusContainsMessage()
    {
        using var capture = new ActivityCapture(nameof(TelemetryNotification));
        var behavior = new ObservabilityDispatchBehavior<TelemetryNotification>(
            new NoOpMetricsCollector(), new ObservabilityOptions { IncludeExceptionMessage = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new TelemetryNotification(), _ => throw new InvalidOperationException("visible"),
                CancellationToken.None));

        Assert.Equal("visible", capture.Single().StatusDescription);
    }

    [Fact]
    public async Task Dispatch_Success_CreatesSpanWithDispatchTag()
    {
        using var capture = new ActivityCapture(nameof(TelemetryNotification));
        var behavior = new ObservabilityDispatchBehavior<TelemetryNotification>(new NoOpMetricsCollector());

        await behavior.Handle(new TelemetryNotification(), _ => Task.CompletedTask, CancellationToken.None);

        var activity = capture.Single();
        Assert.Equal("notification_or_fire_and_forget", Tag(activity, "dispatch.type"));
        Assert.Equal(true, Tag(activity, "request.success"));
    }

    [Fact]
    public void AddObservability_RegistersConfiguredOptions()
    {
        var services = new ServiceCollection();
        services.AddObservability(o => o.IncludeExceptionMessage = true);
        using var provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<ObservabilityOptions>().IncludeExceptionMessage);
        Assert.IsType<NoOpMetricsCollector>(provider.GetRequiredService<IMetricsCollector>());
    }

    [Fact]
    public void AddObservability_Default_HidesExceptionMessages()
    {
        var services = new ServiceCollection();
        services.AddObservability();
        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<ObservabilityOptions>().IncludeExceptionMessage);
    }

    [Fact]
    public void AddObservability_NullConfigure_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddObservability((Action<ObservabilityOptions>)null!));
    }
}
