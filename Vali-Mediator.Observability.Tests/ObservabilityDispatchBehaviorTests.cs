using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.Notification;
using Vali_Mediator_Observability.Pipeline;
using Xunit;

namespace Vali_Mediator_Observability.Tests;

internal sealed class UserCreated : INotification { }

internal sealed class SendEmail : IFireAndForget { }

public class ObservabilityDispatchBehaviorTests
{
    [Fact]
    public async Task Notification_Success_RecordsStartedAndCompleted()
    {
        var metrics = new RecordingMetrics();
        var behavior = new ObservabilityDispatchBehavior<UserCreated>(metrics);
        var handlerRan = false;

        await behavior.Handle(new UserCreated(), _ => { handlerRan = true; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.True(handlerRan);
        Assert.Equal(1, metrics.Started);
        Assert.Equal(1, metrics.Completed);
        Assert.Equal(0, metrics.Failed);
    }

    [Fact]
    public async Task Notification_HandlerThrows_RecordsFailureAndRethrowsOriginal()
    {
        var metrics = new RecordingMetrics();
        var behavior = new ObservabilityDispatchBehavior<UserCreated>(metrics);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new UserCreated(), _ => throw new InvalidOperationException("boom"),
                CancellationToken.None));

        Assert.Equal("boom", ex.Message);
        Assert.Equal(1, metrics.Started);
        Assert.Equal(0, metrics.Completed);
        Assert.Equal(1, metrics.Failed);
    }

    [Fact]
    public async Task FireAndForget_Success_RecordsStartedAndCompleted()
    {
        var metrics = new RecordingMetrics();
        var behavior = new ObservabilityDispatchBehavior<SendEmail>(metrics);

        await behavior.Handle(new SendEmail(), _ => Task.CompletedTask, CancellationToken.None)
            ;

        Assert.Equal(1, metrics.Started);
        Assert.Equal(1, metrics.Completed);
        Assert.Equal(0, metrics.Failed);
    }

    [Fact]
    public async Task FireAndForget_AsyncFailure_RecordsFailureAndRethrows()
    {
        var metrics = new RecordingMetrics();
        var behavior = new ObservabilityDispatchBehavior<SendEmail>(metrics);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new SendEmail(), async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            }, CancellationToken.None));

        Assert.Equal(1, metrics.Failed);
        Assert.Equal(0, metrics.Completed);
    }
}
