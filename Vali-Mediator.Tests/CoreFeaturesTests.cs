using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.General.Behavior;
using Xunit;

namespace Vali_Mediator.Tests;

/// <summary>
/// Tests for features added in v2.0:
/// SendAll, IHasTimeout / TimeoutBehavior, INotificationFilter, and DeadLetterQueue.
/// </summary>
public class CoreFeaturesTests
{
    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildProviderWithHandlers(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(config =>
        {
            config.RegisterServicesFromAssembly(typeof(CoreFeaturesTests).Assembly);
        });
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // =========================================================================
    // 1. SendAll
    // =========================================================================

    private record NumberQuery(int Value) : IRequest<int>;

    private class NumberQueryHandler : IRequestHandler<NumberQuery, int>
    {
        public Task<int> Handle(NumberQuery request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value * 2);
    }

    [Fact]
    public async Task SendAll_ReturnsResultsForAllRequests()
    {
        await using var provider = BuildProvider(s =>
        {
            s.AddTransient<IRequestHandler<NumberQuery, int>, NumberQueryHandler>();
        });

        var mediator = provider.GetRequiredService<IValiMediator>();
        var requests = new[] { new NumberQuery(1), new NumberQuery(2), new NumberQuery(3) };

        int[] results = await mediator.SendAll<int>(requests);

        Assert.Equal(3, results.Length);
        Assert.Contains(2, results);
        Assert.Contains(4, results);
        Assert.Contains(6, results);
    }

    [Fact]
    public async Task SendAll_EmptyEnumerable_ReturnsEmptyArray()
    {
        await using var provider = BuildProvider(s =>
        {
            s.AddTransient<IRequestHandler<NumberQuery, int>, NumberQueryHandler>();
        });

        var mediator = provider.GetRequiredService<IValiMediator>();

        int[] results = await mediator.SendAll<int>(Enumerable.Empty<NumberQuery>());

        Assert.Empty(results);
    }

    [Fact]
    public async Task SendAll_ThrowsWhenNullEnumerable()
    {
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            mediator.SendAll<int>(null!));
    }

    [Fact]
    public async Task SendAll_RunsInParallel_AllResultsCorrect()
    {
        await using var provider = BuildProvider(s =>
        {
            s.AddTransient<IRequestHandler<NumberQuery, int>, NumberQueryHandler>();
        });

        var mediator = provider.GetRequiredService<IValiMediator>();
        var requests = Enumerable.Range(1, 10).Select(i => new NumberQuery(i));

        int[] results = await mediator.SendAll<int>(requests);

        Assert.Equal(10, results.Length);
        for (int i = 1; i <= 10; i++)
            Assert.Contains(i * 2, results);
    }

    // =========================================================================
    // 2. IHasTimeout / TimeoutBehavior
    // =========================================================================

    private record SlowQuery(int DelayMs) : IRequest<string>, IHasTimeout
    {
        public TimeSpan Timeout => TimeSpan.FromMilliseconds(DelayMs / 2.0);
    }

    private class SlowQueryHandler : IRequestHandler<SlowQuery, string>
    {
        public async Task<string> Handle(SlowQuery request, CancellationToken cancellationToken)
        {
            await Task.Delay(request.DelayMs, cancellationToken);
            return "done";
        }
    }

    private record FastQuery(int DelayMs) : IRequest<string>, IHasTimeout
    {
        public TimeSpan Timeout => TimeSpan.FromMilliseconds(DelayMs * 2);
    }

    private class FastQueryHandler : IRequestHandler<FastQuery, string>
    {
        public async Task<string> Handle(FastQuery request, CancellationToken cancellationToken)
        {
            await Task.Delay(request.DelayMs, cancellationToken);
            return "fast-done";
        }
    }

    [Fact]
    public async Task TimeoutBehavior_CancelsWhenOperationExceedsTimeout()
    {
        await using var provider = BuildProvider(s =>
        {
            s.AddTransient<IRequestHandler<SlowQuery, string>, SlowQueryHandler>();
            s.AddTimeoutBehavior();
        });

        var mediator = provider.GetRequiredService<IValiMediator>();
        // Timeout = 50ms, handler delay = 100ms → should cancel
        var query = new SlowQuery(DelayMs: 200);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            mediator.Send(query));
    }

    [Fact]
    public async Task TimeoutBehavior_CompletesWhenOperationFasterThanTimeout()
    {
        await using var provider = BuildProvider(s =>
        {
            s.AddTransient<IRequestHandler<FastQuery, string>, FastQueryHandler>();
            s.AddTimeoutBehavior();
        });

        var mediator = provider.GetRequiredService<IValiMediator>();
        // Timeout = 200ms, handler delay = 50ms → should complete
        var query = new FastQuery(DelayMs: 50);

        string result = await mediator.Send(query);

        Assert.Equal("fast-done", result);
    }

    // =========================================================================
    // 3. INotificationFilter
    // =========================================================================

    private record FilteredEvent(bool ShouldProcess) : INotification;

    private static readonly List<string> FilterLog = new List<string>();

    private class FilteredHandler : INotificationHandler<FilteredEvent>, INotificationFilter<FilteredEvent>
    {
        public bool ShouldHandle(FilteredEvent notification) => notification.ShouldProcess;

        public Task Handle(FilteredEvent notification, CancellationToken cancellationToken)
        {
            FilterLog.Add("handled");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task NotificationFilter_SkipsHandlerWhenShouldHandleReturnsFalse()
    {
        FilterLog.Clear();

        await using var provider = BuildProvider(s =>
        {
            s.AddTransient<INotificationHandler<FilteredEvent>, FilteredHandler>();
        });

        var mediator = provider.GetRequiredService<IValiMediator>();
        await mediator.Publish(new FilteredEvent(ShouldProcess: false));

        Assert.Empty(FilterLog);
    }

    [Fact]
    public async Task NotificationFilter_ExecutesHandlerWhenShouldHandleReturnsTrue()
    {
        FilterLog.Clear();

        await using var provider = BuildProvider(s =>
        {
            s.AddTransient<INotificationHandler<FilteredEvent>, FilteredHandler>();
        });

        var mediator = provider.GetRequiredService<IValiMediator>();
        await mediator.Publish(new FilteredEvent(ShouldProcess: true));

        Assert.Single(FilterLog);
        Assert.Equal("handled", FilterLog[0]);
    }

    // =========================================================================
    // 4. DeadLetterQueue
    // =========================================================================

    private record FailingEvent : INotification;

    private class AlwaysFailsHandler : INotificationHandler<FailingEvent>
    {
        public Task Handle(FailingEvent notification, CancellationToken cancellationToken)
            => throw new InvalidOperationException("handler failure");
    }

    private class SucceedsHandler : INotificationHandler<FailingEvent>
    {
        public static int CallCount;
        public Task Handle(FailingEvent notification, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DeadLetterQueue_CapturesFailedHandlerWithoutThrowing()
    {
        SucceedsHandler.CallCount = 0;

        await using var provider = BuildProvider(s =>
        {
            s.AddTransient<INotificationHandler<FailingEvent>, AlwaysFailsHandler>();
            s.AddTransient<INotificationHandler<FailingEvent>, SucceedsHandler>();
            s.AddInMemoryDeadLetterQueue();
        });

        var mediator = provider.GetRequiredService<IValiMediator>();
        var dlq = provider.GetRequiredService<IDeadLetterQueue>();

        // Should NOT throw because DLQ captures the failure
        await mediator.Publish(new FailingEvent(), PublishStrategy.ResilientParallel);

        // Successful handler still ran
        Assert.Equal(1, SucceedsHandler.CallCount);

        // Failed handler's entry is in the DLQ
        var entries = ((InMemoryDeadLetterQueue)dlq).GetEntries();
        Assert.Single(entries);
        Assert.IsType<InvalidOperationException>(entries[0].Exception);
    }

    [Fact]
    public async Task DeadLetterQueue_WithoutDlqRegistered_ThrowsAggregateException()
    {
        await using var provider = BuildProvider(s =>
        {
            s.AddTransient<INotificationHandler<FailingEvent>, AlwaysFailsHandler>();
        });

        var mediator = provider.GetRequiredService<IValiMediator>();

        // Without DLQ, exceptions propagate
        await Assert.ThrowsAnyAsync<Exception>(() =>
            mediator.Publish(new FailingEvent(), PublishStrategy.ResilientParallel));
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_RespectsMaxEntries()
    {
        await using var provider = BuildProvider(s =>
        {
            s.AddInMemoryDeadLetterQueue(maxEntries: 2);
        });

        var dlq = provider.GetRequiredService<IDeadLetterQueue>() as InMemoryDeadLetterQueue;
        Assert.NotNull(dlq);

        // Enqueue 3 entries — only last 2 should remain
        for (int i = 0; i < 3; i++)
        {
            var entry = new DeadLetterEntry
            {
                NotificationTypeName = "TestEvent",
                HandlerTypeName = $"Handler{i}",
                Exception = new Exception($"error {i}"),
                FailedAt = DateTimeOffset.UtcNow,
                Notification = new FailingEvent()
            };
            await dlq.EnqueueAsync(entry);
        }

        Assert.Equal(2, dlq.GetEntries().Count);
    }
}
