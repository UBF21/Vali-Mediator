using Vali_Mediator.Core.Request;
using Vali_Mediator_Observability.Core.Abstractions;
using Vali_Mediator_Observability.Core.Context;
using Vali_Mediator_Observability.Core.Metrics;
using Vali_Mediator_Observability.Pipeline;
using Xunit;

namespace Vali_Mediator_Observability.Tests;

// ---------------------------------------------------------------------------
// Test stubs
// ---------------------------------------------------------------------------

internal sealed class PingRequest : IRequest<string> { }

internal sealed class TrackingObserver : IRequestObserver
{
    public List<string> Events { get; } = new List<string>();
    public ObservabilityContext? LastContext { get; private set; }

    public Task OnStarted(ObservabilityContext context, CancellationToken ct = default)
    {
        Events.Add("started");
        LastContext = context;
        return Task.CompletedTask;
    }

    public Task OnCompleted(ObservabilityContext context, CancellationToken ct = default)
    {
        Events.Add("completed");
        LastContext = context;
        return Task.CompletedTask;
    }

    public Task OnFailed(ObservabilityContext context, CancellationToken ct = default)
    {
        Events.Add("failed");
        LastContext = context;
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingObserver : IRequestObserver
{
    public bool WasCalled { get; private set; }

    public Task OnStarted(ObservabilityContext context, CancellationToken ct = default)
    {
        WasCalled = true;
        throw new InvalidOperationException("observer boom");
    }

    public Task OnCompleted(ObservabilityContext context, CancellationToken ct = default)
    {
        WasCalled = true;
        throw new InvalidOperationException("observer boom");
    }

    public Task OnFailed(ObservabilityContext context, CancellationToken ct = default)
    {
        WasCalled = true;
        throw new InvalidOperationException("observer boom");
    }
}

internal sealed class CountingObserver : IRequestObserver
{
    public int StartedCount { get; private set; }
    public int CompletedCount { get; private set; }
    public int FailedCount { get; private set; }

    public Task OnStarted(ObservabilityContext context, CancellationToken ct = default)
    {
        StartedCount++;
        return Task.CompletedTask;
    }

    public Task OnCompleted(ObservabilityContext context, CancellationToken ct = default)
    {
        CompletedCount++;
        return Task.CompletedTask;
    }

    public Task OnFailed(ObservabilityContext context, CancellationToken ct = default)
    {
        FailedCount++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingMetrics : IMetricsCollector
{
    public int Started { get; private set; }
    public int Completed { get; private set; }
    public int Failed { get; private set; }

    public void RecordRequestStarted(string requestName) => Started++;
    public void RecordRequestCompleted(string requestName, TimeSpan duration, bool success) => Completed++;
    public void RecordRequestFailed(string requestName, TimeSpan duration, string exceptionType) => Failed++;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class ObservabilityBehaviorTests
{
    private static ObservabilityBehavior<PingRequest, string> CreateBehavior(
        IEnumerable<IRequestObserver> observers,
        IMetricsCollector? metrics = null)
        => new ObservabilityBehavior<PingRequest, string>(observers, metrics ?? new NoOpMetricsCollector());

    // -----------------------------------------------------------------------
    // Observer called on success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Observer_IsCalledOnStarted_WhenRequestSucceeds()
    {
        var observer = new TrackingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { observer });

        await behavior.Handle(new PingRequest(), _ => Task.FromResult("pong"), CancellationToken.None)
            ;

        Assert.Contains("started", observer.Events);
    }

    [Fact]
    public async Task Observer_IsCalledOnCompleted_WhenRequestSucceeds()
    {
        var observer = new TrackingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { observer });

        await behavior.Handle(new PingRequest(), _ => Task.FromResult("pong"), CancellationToken.None)
            ;

        Assert.Contains("completed", observer.Events);
        Assert.DoesNotContain("failed", observer.Events);
    }

    // -----------------------------------------------------------------------
    // Observer called on failure
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Observer_IsCalledOnFailed_WhenHandlerThrows()
    {
        var observer = new TrackingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { observer });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(
                new PingRequest(),
                _ => throw new InvalidOperationException("boom"),
                CancellationToken.None));

        Assert.Contains("failed", observer.Events);
        Assert.DoesNotContain("completed", observer.Events);
    }

    // -----------------------------------------------------------------------
    // Duration is populated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Context_DurationIsPopulated_AfterSuccessfulRequest()
    {
        var observer = new TrackingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { observer });

        await behavior.Handle(new PingRequest(), _ => Task.FromResult("pong"), CancellationToken.None)
            ;

        Assert.NotNull(observer.LastContext?.Duration);
        Assert.True(observer.LastContext!.Duration!.Value.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task Context_DurationIsPopulated_AfterFailedRequest()
    {
        var observer = new TrackingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { observer });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(
                new PingRequest(),
                _ => throw new InvalidOperationException("boom"),
                CancellationToken.None));

        Assert.NotNull(observer.LastContext?.Duration);
    }

    // -----------------------------------------------------------------------
    // Multiple observers — all called
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MultipleObservers_AllCalledOnCompleted()
    {
        var first = new CountingObserver();
        var second = new CountingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { first, second });

        await behavior.Handle(new PingRequest(), _ => Task.FromResult("pong"), CancellationToken.None)
            ;

        Assert.Equal(1, first.StartedCount);
        Assert.Equal(1, first.CompletedCount);
        Assert.Equal(1, second.StartedCount);
        Assert.Equal(1, second.CompletedCount);
    }

    [Fact]
    public async Task MultipleObservers_AllCalledOnFailed()
    {
        var first = new CountingObserver();
        var second = new CountingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { first, second });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(
                new PingRequest(),
                _ => throw new InvalidOperationException("boom"),
                CancellationToken.None));

        Assert.Equal(1, first.FailedCount);
        Assert.Equal(1, second.FailedCount);
    }

    // -----------------------------------------------------------------------
    // Exception in one observer does not stop others
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExceptionInOneObserver_DoesNotPreventOthersFromRunning_OnStarted()
    {
        var throwing = new ThrowingObserver();
        var counting = new CountingObserver();

        // Throwing observer is first; counting must still run.
        var behavior = CreateBehavior(new List<IRequestObserver> { throwing, counting });

        var result = await behavior.Handle(new PingRequest(), _ => Task.FromResult("pong"), CancellationToken.None)
            ;

        Assert.Equal("pong", result);
        Assert.True(throwing.WasCalled);
        Assert.Equal(1, counting.StartedCount);
        Assert.Equal(1, counting.CompletedCount);
    }

    [Fact]
    public async Task ThrowingObserver_OnStarted_DoesNotPreventHandlerFromRunning()
    {
        var handlerRan = false;
        var behavior = CreateBehavior(new List<IRequestObserver> { new ThrowingObserver() });

        await behavior.Handle(new PingRequest(), _ => { handlerRan = true; return Task.FromResult("pong"); },
            CancellationToken.None);

        Assert.True(handlerRan);
    }

    [Fact]
    public async Task ThrowingObserver_OnCompleted_DoesNotCountRequestAsFailure()
    {
        var metrics = new RecordingMetrics();
        var counting = new CountingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { new ThrowingObserver(), counting }, metrics);

        var result = await behavior.Handle(new PingRequest(), _ => Task.FromResult("pong"), CancellationToken.None)
            ;

        Assert.Equal("pong", result);
        Assert.Equal(1, metrics.Completed);
        Assert.Equal(0, metrics.Failed);
        Assert.Equal(0, counting.FailedCount);
    }

    [Fact]
    public async Task ThrowingObserver_OnFailed_DoesNotReplaceOriginalException()
    {
        var counting = new CountingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { new ThrowingObserver(), counting });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new PingRequest(), _ => throw new InvalidOperationException("handler boom"),
                CancellationToken.None));

        Assert.Equal("handler boom", ex.Message);
        Assert.Equal(1, counting.FailedCount);
    }

    // -----------------------------------------------------------------------
    // Context fields
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Context_RequestNameMatchesTypeName()
    {
        var observer = new TrackingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { observer });

        await behavior.Handle(new PingRequest(), _ => Task.FromResult("pong"), CancellationToken.None)
            ;

        Assert.Equal("PingRequest", observer.LastContext?.RequestName);
    }

    [Fact]
    public async Task Context_OperationIdIsNotNull()
    {
        var observer = new TrackingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { observer });

        await behavior.Handle(new PingRequest(), _ => Task.FromResult("pong"), CancellationToken.None)
            ;

        Assert.NotNull(observer.LastContext?.OperationId);
        Assert.NotEmpty(observer.LastContext!.OperationId!);
    }

    [Fact]
    public async Task Context_ResponseIsPopulated_OnSuccess()
    {
        var observer = new TrackingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { observer });

        await behavior.Handle(new PingRequest(), _ => Task.FromResult("pong"), CancellationToken.None)
            ;

        Assert.Equal("pong", observer.LastContext?.Response);
    }

    [Fact]
    public async Task Context_ExceptionIsPopulated_OnFailure()
    {
        var observer = new TrackingObserver();
        var behavior = CreateBehavior(new List<IRequestObserver> { observer });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(
                new PingRequest(),
                _ => throw new InvalidOperationException("boom"),
                CancellationToken.None));

        Assert.NotNull(observer.LastContext?.Exception);
        Assert.IsType<InvalidOperationException>(observer.LastContext!.Exception);
    }
}
