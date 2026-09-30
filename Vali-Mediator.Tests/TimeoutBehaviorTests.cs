using System.Diagnostics;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;
using Xunit;

namespace Vali_Mediator.Tests;

public class TimeoutBehaviorTests
{
    private record TimedQuery(TimeSpan Timeout) : IRequest<string>, IHasTimeout;

    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task CallerCancellation_PropagatesAsOperationCanceled_NotTimeout()
    {
        var behavior = new TimeoutBehavior<TimedQuery, string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var sw = Stopwatch.StartNew();

        // Handler ignores the token on purpose: the behavior itself must observe the caller's cancellation.
        var act = () => behavior.Handle(
            new TimedQuery(Long),
            async _ => { await Task.Delay(2000); return "late"; },
            cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Timeout_ThrowsTimeoutException_WithRequestName()
    {
        var behavior = new TimeoutBehavior<TimedQuery, string>();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => behavior.Handle(
            new TimedQuery(TimeSpan.FromMilliseconds(50)),
            async _ => { await Task.Delay(2000); return "late"; },
            CancellationToken.None));

        Assert.Contains(nameof(TimedQuery), ex.Message);
    }

    [Fact]
    public async Task FastHandler_ReturnsResult_AndHandlerExceptionsPropagateUnchanged()
    {
        var behavior = new TimeoutBehavior<TimedQuery, string>();

        var ok = await behavior.Handle(new TimedQuery(Long), _ => Task.FromResult("ok"), CancellationToken.None);
        Assert.Equal("ok", ok);

        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new TimedQuery(Long),
            _ => Task.FromException<string>(new InvalidOperationException("boom")),
            CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_CancelsTokenPassedToHandler()
    {
        var behavior = new TimeoutBehavior<TimedQuery, string>();
        CancellationToken received = default;
        var handlerObservedCancel = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var act = () => behavior.Handle(
            new TimedQuery(TimeSpan.FromMilliseconds(50)),
            async ct =>
            {
                received = ct;
                try { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { handlerObservedCancel.SetResult(true); throw; }
                return "late";
            },
            CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(act);
        Assert.True(received.CanBeCanceled);
        Assert.True(await handlerObservedCancel.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task CallerCancellation_IsObservedByHandlerToken()
    {
        var behavior = new TimeoutBehavior<TimedQuery, string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(
            new TimedQuery(Long),
            async ct => { await Task.Delay(5000, ct); return "late"; },
            cts.Token));
    }
}
