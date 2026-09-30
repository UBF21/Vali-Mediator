using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Exceptions;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Streaming;
using Xunit;

namespace Vali_Mediator.Tests;

public class CoverageGapTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private static ServiceProvider BuildProvider(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        register(services);
        return services.BuildServiceProvider();
    }

    // ---- SendOrDefault ----

    private record Ping(int Value) : IRequest<int>;

    private class PingHandler : IRequestHandler<Ping, int>
    {
        public Task<int> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult(request.Value * 2);
    }

    [Fact]
    public async Task SendOrDefault_NoHandler_ReturnsDefault()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<IValiMediator>();

        Assert.Equal(0, await mediator.SendOrDefault(new Ping(4)));
    }

    [Fact]
    public async Task SendOrDefault_WithHandler_ReturnsHandlerResult()
    {
        using var provider = BuildProvider(s => s.AddTransient<IRequestHandler<Ping, int>, PingHandler>());
        var mediator = provider.GetRequiredService<IValiMediator>();

        Assert.Equal(8, await mediator.SendOrDefault(new Ping(4)));
    }

    [Fact]
    public async Task Send_NoHandler_ThrowsHandlerNotFound()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<HandlerNotFoundException>(() => mediator.Send(new Ping(1)));
    }

    // ---- SendAll ----

    [Fact]
    public async Task SendAll_ReturnsResponsesInRequestOrder()
    {
        using var provider = BuildProvider(s => s.AddTransient<IRequestHandler<Ping, int>, PingHandler>());
        var mediator = provider.GetRequiredService<IValiMediator>();

        var results = await mediator.SendAll(Enumerable.Range(1, 10).Select(i => (IRequest<int>)new Ping(i)));

        Assert.Equal(Enumerable.Range(1, 10).Select(i => i * 2).ToArray(), results);
    }

    // ---- Send under concurrency ----

    [Fact]
    public async Task Send_ManyConcurrentCalls_AllReturnCorrectResponse()
    {
        using var provider = BuildProvider(s => s.AddTransient<IRequestHandler<Ping, int>, PingHandler>());
        var mediator = provider.GetRequiredService<IValiMediator>();

        var tasks = Enumerable.Range(0, 500)
            .Select(i => Task.Run(() => mediator.Send(new Ping(i))))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++)
            Assert.Equal(i * 2, results[i]);
    }

    // ---- Streaming ----

    private record Numbers(int Count) : IStreamRequest<int>;

    private class NumbersHandler : IStreamRequestHandler<Numbers, int>
    {
        public async IAsyncEnumerable<int> Handle(Numbers request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < request.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return i;
            }
        }
    }

    [Fact]
    public async Task CreateStream_YieldsAllItems()
    {
        using var provider = BuildProvider(s => s.AddTransient<IStreamRequestHandler<Numbers, int>, NumbersHandler>());
        var mediator = provider.GetRequiredService<IValiMediator>();

        var items = new List<int>();
        await foreach (var n in mediator.CreateStream(new Numbers(5)))
            items.Add(n);

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, items);
    }

    [Fact]
    public void CreateStream_NoHandler_ThrowsHandlerNotFound()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<IValiMediator>();

        Assert.Throws<HandlerNotFoundException>(() => mediator.CreateStream(new Numbers(1)));
    }

    [Fact]
    public async Task CreateStream_CancelledToken_StopsEnumeration()
    {
        using var provider = BuildProvider(s => s.AddTransient<IStreamRequestHandler<Numbers, int>, NumbersHandler>());
        var mediator = provider.GetRequiredService<IValiMediator>();
        using var cts = new CancellationTokenSource();

        var seen = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in mediator.CreateStream(new Numbers(1000), cts.Token))
            {
                if (++seen == 3) cts.Cancel();
            }
        });
        Assert.Equal(3, seen);
    }

    // ---- Notifications: strategies, priority, filter, DLQ ----

    private record Evt(string Name) : INotification;

    private class Log
    {
        public List<string> Entries { get; } = new();
        public void Add(string s) { lock (Entries) Entries.Add(s); }
    }

    private class HighHandler : INotificationHandler<Evt>
    {
        private readonly Log _log;
        public HighHandler(Log log) => _log = log;
        public int Priority => 10;
        public Task Handle(Evt notification, CancellationToken cancellationToken) { _log.Add("high"); return Task.CompletedTask; }
    }

    private class LowHandler : INotificationHandler<Evt>
    {
        private readonly Log _log;
        public LowHandler(Log log) => _log = log;
        public int Priority => 1;
        public Task Handle(Evt notification, CancellationToken cancellationToken) { _log.Add("low"); return Task.CompletedTask; }
    }

    private class SkippingHandler : INotificationHandler<Evt>, INotificationFilter<Evt>
    {
        private readonly Log _log;
        public SkippingHandler(Log log) => _log = log;
        public bool ShouldHandle(Evt notification) => notification.Name == "go";
        public Task Handle(Evt notification, CancellationToken cancellationToken) { _log.Add("filtered:" + notification.Name); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Publish_Sequential_RunsByDescendingPriority()
    {
        var log = new Log();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<INotificationHandler<Evt>, LowHandler>();
            s.AddTransient<INotificationHandler<Evt>, HighHandler>();
        });

        await provider.GetRequiredService<IValiMediator>().Publish(new Evt("x"));

        Assert.Equal(new[] { "high", "low" }, log.Entries.ToArray());
    }

    [Fact]
    public async Task Publish_NotificationFilter_SkipsHandlerWhenShouldHandleIsFalse()
    {
        var log = new Log();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<INotificationHandler<Evt>, SkippingHandler>();
        });
        var mediator = provider.GetRequiredService<IValiMediator>();

        await mediator.Publish(new Evt("stop"));
        await mediator.Publish(new Evt("go"));

        Assert.Equal(new[] { "filtered:go" }, log.Entries.ToArray());
    }

    private class RendezvousState
    {
        public readonly TaskCompletionSource A = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource B = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private class RendezvousA : INotificationHandler<Evt>
    {
        private readonly RendezvousState _state;
        public RendezvousA(RendezvousState state) => _state = state;

        public async Task Handle(Evt notification, CancellationToken cancellationToken)
        {
            _state.A.SetResult();
            await _state.B.Task.WaitAsync(Deadline, cancellationToken);
        }
    }

    private class RendezvousB : INotificationHandler<Evt>
    {
        private readonly RendezvousState _state;
        public RendezvousB(RendezvousState state) => _state = state;

        public async Task Handle(Evt notification, CancellationToken cancellationToken)
        {
            _state.B.SetResult();
            await _state.A.Task.WaitAsync(Deadline, cancellationToken);
        }
    }

    [Fact]
    public async Task Publish_Parallel_RunsHandlersConcurrently()
    {
        var state = new RendezvousState();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(state);
            s.AddTransient<INotificationHandler<Evt>, RendezvousA>();
            s.AddTransient<INotificationHandler<Evt>, RendezvousB>();
        });

        // Each handler waits for the other: only completes when both run at the same time.
        var publish = provider.GetRequiredService<IValiMediator>().Publish(new Evt("x"), PublishStrategy.Parallel);
        var finished = await Task.WhenAny(publish, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(publish, finished);
        await publish;
    }

    private class FailingHandler : INotificationHandler<Evt>
    {
        public int Priority => 5;
        public Task Handle(Evt notification, CancellationToken cancellationToken)
            => throw new InvalidOperationException("handler failed");
    }

    [Fact]
    public async Task Publish_ResilientParallel_RunsAllHandlersAndSendsFailuresToDlq()
    {
        var log = new Log();
        var dlq = new InMemoryDeadLetterQueue();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddSingleton<IDeadLetterQueue>(dlq);
            s.AddTransient<INotificationHandler<Evt>, FailingHandler>();
            s.AddTransient<INotificationHandler<Evt>, LowHandler>();
        });

        await provider.GetRequiredService<IValiMediator>().Publish(new Evt("x"), PublishStrategy.ResilientParallel);

        Assert.Equal(new[] { "low" }, log.Entries.ToArray());
        var entry = Assert.Single(dlq.GetEntries());
        Assert.Contains(nameof(FailingHandler), entry.HandlerTypeName);
        Assert.Contains(nameof(Evt), entry.NotificationTypeName);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task Publish_ResilientParallel_WithoutDlq_ThrowsFailure()
    {
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(new Log());
            s.AddTransient<INotificationHandler<Evt>, FailingHandler>();
            s.AddTransient<INotificationHandler<Evt>, LowHandler>();
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<IValiMediator>().Publish(new Evt("x"), PublishStrategy.ResilientParallel));
    }

    [Fact]
    public async Task DeadLetterQueue_DropsOldestBeyondMaxEntries()
    {
        var dlq = new InMemoryDeadLetterQueue(2);
        for (var i = 0; i < 4; i++)
            await dlq.EnqueueAsync(new DeadLetterEntry { HandlerTypeName = "h" + i, Exception = new Exception(), Notification = new object() });

        Assert.Equal(new[] { "h2", "h3" }, dlq.GetEntries().Select(e => e.HandlerTypeName).ToArray());
    }
}
