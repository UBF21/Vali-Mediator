using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Xunit;

namespace Vali_Mediator.Tests;

public class NotificationTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // -------------------------------------------------------------------------
    // Inline types
    // -------------------------------------------------------------------------

    private record SimpleEvent(string Tag) : INotification;

    private record PriorityEvent : INotification;

    // Handler that appends to a shared list
    private class ListenerA : INotificationHandler<SimpleEvent>
    {
        private readonly List<string> _log;
        public ListenerA(List<string> log) => _log = log;
        public Task Handle(SimpleEvent n, CancellationToken ct) { _log.Add("A"); return Task.CompletedTask; }
    }

    private class ListenerB : INotificationHandler<SimpleEvent>
    {
        private readonly List<string> _log;
        public ListenerB(List<string> log) => _log = log;
        public Task Handle(SimpleEvent n, CancellationToken ct) { _log.Add("B"); return Task.CompletedTask; }
    }

    private class PriorityHighHandler : INotificationHandler<PriorityEvent>
    {
        private readonly List<string> _log;
        public PriorityHighHandler(List<string> log) => _log = log;
        public int Priority => 10;
        public Task Handle(PriorityEvent n, CancellationToken ct) { _log.Add("high"); return Task.CompletedTask; }
    }

    private class PriorityLowHandler : INotificationHandler<PriorityEvent>
    {
        private readonly List<string> _log;
        public PriorityLowHandler(List<string> log) => _log = log;
        public int Priority => 1;
        public Task Handle(PriorityEvent n, CancellationToken ct) { _log.Add("low"); return Task.CompletedTask; }
    }

    private class ThrowingHandler : INotificationHandler<SimpleEvent>
    {
        private readonly List<string> _log;
        public ThrowingHandler(List<string> log) => _log = log;
        public async Task Handle(SimpleEvent n, CancellationToken ct)
        {
            await Task.Yield();
            _log.Add("threw");
            throw new InvalidOperationException("handler error");
        }
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Publish_Sequential_AllHandlersRun()
    {
        var log = new List<string>();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<INotificationHandler<SimpleEvent>, ListenerA>();
            s.AddTransient<INotificationHandler<SimpleEvent>, ListenerB>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await mediator.Publish(new SimpleEvent("t"), PublishStrategy.Sequential);

        Assert.Contains("A", log);
        Assert.Contains("B", log);
    }

    [Fact]
    public async Task Publish_Sequential_RespectsPriorityOrder()
    {
        var log = new List<string>();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<INotificationHandler<PriorityEvent>, PriorityLowHandler>();
            s.AddTransient<INotificationHandler<PriorityEvent>, PriorityHighHandler>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await mediator.Publish(new PriorityEvent(), PublishStrategy.Sequential);

        Assert.Equal("high", log[0]);
        Assert.Equal("low", log[1]);
    }

    [Fact]
    public async Task Publish_Parallel_AllHandlersRun()
    {
        var log = new System.Collections.Concurrent.ConcurrentBag<string>();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<INotificationHandler<SimpleEvent>, ConcurrentListenerA>();
            s.AddTransient<INotificationHandler<SimpleEvent>, ConcurrentListenerB>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await mediator.Publish(new SimpleEvent("p"), PublishStrategy.Parallel);

        Assert.Contains("CA", log);
        Assert.Contains("CB", log);
    }

    private class ConcurrentListenerA : INotificationHandler<SimpleEvent>
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<string> _log;
        public ConcurrentListenerA(System.Collections.Concurrent.ConcurrentBag<string> log) => _log = log;
        public Task Handle(SimpleEvent n, CancellationToken ct) { _log.Add("CA"); return Task.CompletedTask; }
    }

    private class ConcurrentListenerB : INotificationHandler<SimpleEvent>
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<string> _log;
        public ConcurrentListenerB(System.Collections.Concurrent.ConcurrentBag<string> log) => _log = log;
        public Task Handle(SimpleEvent n, CancellationToken ct) { _log.Add("CB"); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Publish_Parallel_ExceptionsAggregated()
    {
        var log = new List<string>();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<INotificationHandler<SimpleEvent>, ThrowingHandler>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAnyAsync<Exception>(
            () => mediator.Publish(new SimpleEvent("t"), PublishStrategy.Parallel));
    }

    [Fact]
    public async Task Publish_ResilientParallel_AllHandlersRunEvenIfOneFails()
    {
        var log = new System.Collections.Concurrent.ConcurrentBag<string>();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<INotificationHandler<SimpleEvent>, ResilientGoodHandler>();
            s.AddTransient<INotificationHandler<SimpleEvent>, ResilientBadHandler>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        try
        {
            await mediator.Publish(new SimpleEvent("r"), PublishStrategy.ResilientParallel);
        }
        catch
        {
            // expected
        }

        Assert.Contains("good", log);
        Assert.Contains("bad-threw", log);
    }

    private class ResilientGoodHandler : INotificationHandler<SimpleEvent>
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<string> _log;
        public ResilientGoodHandler(System.Collections.Concurrent.ConcurrentBag<string> log) => _log = log;
        public Task Handle(SimpleEvent n, CancellationToken ct) { _log.Add("good"); return Task.CompletedTask; }
    }

    private class ResilientBadHandler : INotificationHandler<SimpleEvent>
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<string> _log;
        public ResilientBadHandler(System.Collections.Concurrent.ConcurrentBag<string> log) => _log = log;
        public async Task Handle(SimpleEvent n, CancellationToken ct)
        {
            await Task.Yield();
            _log.Add("bad-threw");
            throw new InvalidOperationException("bad");
        }
    }

    [Fact]
    public async Task Publish_ResilientParallel_ThrowsAggregateExceptionWhenHandlersFail()
    {
        var log = new System.Collections.Concurrent.ConcurrentBag<string>();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<INotificationHandler<SimpleEvent>, ResilientBadHandler>();
            s.AddTransient<INotificationHandler<SimpleEvent>, AnotherBadHandler>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => mediator.Publish(new SimpleEvent("r"), PublishStrategy.ResilientParallel));

        Assert.Equal(2, ex.InnerExceptions.Count);
    }

    private class AnotherBadHandler : INotificationHandler<SimpleEvent>
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<string> _log;
        public AnotherBadHandler(System.Collections.Concurrent.ConcurrentBag<string> log) => _log = log;
        public async Task Handle(SimpleEvent n, CancellationToken ct)
        {
            await Task.Yield();
            _log.Add("another-bad");
            throw new InvalidOperationException("another bad");
        }
    }

    [Fact]
    public async Task Publish_NoHandlers_NoException()
    {
        await using var sp = BuildProvider();

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        // Should complete without throwing
        await mediator.Publish(new SimpleEvent("no-handlers"));
    }
}
