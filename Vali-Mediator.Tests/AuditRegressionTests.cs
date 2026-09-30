using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Xunit;

namespace Vali_Mediator.Tests;

public class AuditRegressionTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        register(services);
        return services.BuildServiceProvider();
    }

    // ---- AUD-C-08: Result.Fail(None) keeps its ErrorType, only default(Result) reports Failure ----

    [Fact]
    public void ResultOfT_FailWithNone_KeepsNone()
    {
        var result = Result<int>.Fail("x", ErrorType.None);

        Assert.Equal(ErrorType.None, result.ErrorType);
        Assert.Equal("x", result.Error);
    }

    [Fact]
    public void ResultOfT_Default_ReportsFailure()
    {
        Assert.Equal(ErrorType.Failure, default(Result<int>).ErrorType);
        Assert.False(default(Result<int>).IsSuccess);
    }

    [Fact]
    public void Result_FailWithNone_KeepsNone()
    {
        Assert.Equal(ErrorType.None, Result.Fail("x", ErrorType.None).ErrorType);
        Assert.Equal(ErrorType.Failure, default(Result).ErrorType);
    }

    // ---- AUD-C-06: scanning the same assembly twice with different lifetimes ----

    public record ScanRequest : IRequest<int>;

    public class ScanHandler : IRequestHandler<ScanRequest, int>
    {
        public Task<int> Handle(ScanRequest request, CancellationToken cancellationToken) => Task.FromResult(1);
    }

    [Fact]
    public void Scan_AddValiMediatorTwiceOnSameCollection_LastLifetimeWins_AndNoDuplicates()
    {
        var services = new ServiceCollection();
        services.AddValiMediator(c => c.RegisterServicesFromAssembly(typeof(ScanHandler).Assembly, ServiceLifetime.Singleton));
        services.AddValiMediator(c => c.RegisterServicesFromAssembly(typeof(ScanHandler).Assembly, ServiceLifetime.Transient));

        var single = Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<ScanRequest, int>));
        Assert.Equal(ServiceLifetime.Transient, single.Lifetime);
    }

    [Fact]
    public void Scan_SameAssemblyTwice_LastLifetimeWins_AndNoDuplicates()
    {
        var services = new ServiceCollection();
        services.AddValiMediator(config =>
        {
            config.RegisterServicesFromAssembly(typeof(ScanHandler).Assembly, ServiceLifetime.Singleton);
            config.RegisterServicesFromAssembly(typeof(ScanHandler).Assembly, ServiceLifetime.Transient);
        });

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IRequestHandler<ScanRequest, int>))
            .ToList();

        var single = Assert.Single(descriptors);
        Assert.Equal(ServiceLifetime.Transient, single.Lifetime);
    }

    // ---- AUD-C-02: Publish runs the union of static-type and runtime-type handlers ----

    public interface IBaseEvent : INotification { }

    public record DerivedEvent : IBaseEvent;

    public class Recorder
    {
        public List<string> Calls { get; } = new();
    }

    public class BaseHandler : INotificationHandler<IBaseEvent>
    {
        private readonly Recorder _recorder;
        public BaseHandler(Recorder recorder) => _recorder = recorder;

        public Task Handle(IBaseEvent notification, CancellationToken cancellationToken)
        {
            _recorder.Calls.Add("base");
            return Task.CompletedTask;
        }
    }

    public class DerivedHandler : INotificationHandler<DerivedEvent>
    {
        private readonly Recorder _recorder;
        public DerivedHandler(Recorder recorder) => _recorder = recorder;

        public Task Handle(DerivedEvent notification, CancellationToken cancellationToken)
        {
            _recorder.Calls.Add("derived");
            return Task.CompletedTask;
        }
    }

    public class BothHandler : INotificationHandler<IBaseEvent>, INotificationHandler<DerivedEvent>
    {
        private readonly Recorder _recorder;
        public BothHandler(Recorder recorder) => _recorder = recorder;

        Task INotificationHandler<IBaseEvent>.Handle(IBaseEvent notification, CancellationToken cancellationToken)
        {
            _recorder.Calls.Add("both");
            return Task.CompletedTask;
        }

        Task INotificationHandler<DerivedEvent>.Handle(DerivedEvent notification, CancellationToken cancellationToken)
        {
            _recorder.Calls.Add("both");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publish_StaticBaseAndRuntimeDerived_RunsBothHandlerSets()
    {
        var recorder = new Recorder();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(recorder);
            s.AddTransient<INotificationHandler<IBaseEvent>, BaseHandler>();
            s.AddTransient<INotificationHandler<DerivedEvent>, DerivedHandler>();
        });
        var mediator = provider.GetRequiredService<IValiMediator>();

        await mediator.Publish<IBaseEvent>(new DerivedEvent());

        Assert.Equal(new[] { "base", "derived" }, recorder.Calls.OrderBy(c => c).ToArray());
    }

    [Fact]
    public async Task Publish_HandlerImplementingBothInterfaces_RunsOnce()
    {
        var recorder = new Recorder();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(recorder);
            s.AddTransient<INotificationHandler<IBaseEvent>, BothHandler>();
            s.AddTransient<INotificationHandler<DerivedEvent>, BothHandler>();
        });
        var mediator = provider.GetRequiredService<IValiMediator>();

        await mediator.Publish<IBaseEvent>(new DerivedEvent());

        Assert.Equal(new[] { "both" }, recorder.Calls.ToArray());
    }

    [Fact]
    public async Task Publish_SameStaticAndRuntimeType_DoesNotDuplicate()
    {
        var recorder = new Recorder();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(recorder);
            s.AddTransient<INotificationHandler<DerivedEvent>, DerivedHandler>();
        });
        var mediator = provider.GetRequiredService<IValiMediator>();

        await mediator.Publish(new DerivedEvent());

        Assert.Equal(new[] { "derived" }, recorder.Calls.ToArray());
    }

    // ---- AUD-C-09: timeout with non-cancellation exception ----

    private record TimedRequest(TimeSpan Timeout) : IRequest<int>, IHasTimeout;

    [Fact]
    public async Task Timeout_HandlerWrapsCancellationInOtherException_StillThrowsTimeoutException()
    {
        var behavior = new TimeoutBehavior<TimedRequest, int>();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => behavior.Handle(
            new TimedRequest(TimeSpan.FromMilliseconds(50)),
            async ct =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    throw new InvalidOperationException("driver wrapped cancellation");
                }

                return 0;
            },
            CancellationToken.None));

        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task Timeout_HandlerIgnoresTokenAndFailsLater_FailureIsObserved()
    {
        var unobserved = new List<Exception>();
        void OnUnobserved(object? s, UnobservedTaskExceptionEventArgs e)
        {
            foreach (var inner in e.Exception.InnerExceptions)
                if (inner.Message == "late failure") lock (unobserved) unobserved.Add(inner);
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var behavior = new TimeoutBehavior<TimedRequest, int>();

            await Assert.ThrowsAsync<TimeoutException>(() => behavior.Handle(
                new TimedRequest(TimeSpan.FromMilliseconds(30)),
                _ => release.Task,
                CancellationToken.None));

            release.SetException(new InvalidOperationException("late failure"));
            release = null!;

            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(20);
            }

            lock (unobserved) Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    // ---- SendAll with a concurrency limit ----

    public record SlowEcho(int Value) : IRequest<int>;

    public class GateProbe
    {
        private int _current;
        public int Max;

        public async Task Enter()
        {
            var now = Interlocked.Increment(ref _current);
            int seen;
            while (now > (seen = Volatile.Read(ref Max)))
                if (Interlocked.CompareExchange(ref Max, now, seen) == seen) break;
            await Task.Yield();
            await Task.Delay(20);
            Interlocked.Decrement(ref _current);
        }
    }

    public class SlowEchoHandler : IRequestHandler<SlowEcho, int>
    {
        private readonly GateProbe _probe;
        public SlowEchoHandler(GateProbe probe) => _probe = probe;

        public async Task<int> Handle(SlowEcho request, CancellationToken cancellationToken)
        {
            await _probe.Enter();
            return request.Value;
        }
    }

    [Fact]
    public async Task SendAll_WithLimit_NeverExceedsLimit_AndKeepsOrder()
    {
        var probe = new GateProbe();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(probe);
            s.AddTransient<IRequestHandler<SlowEcho, int>, SlowEchoHandler>();
        });
        var mediator = provider.GetRequiredService<IValiMediator>();

        var results = await mediator.SendAll(
            Enumerable.Range(0, 20).Select(i => (IRequest<int>)new SlowEcho(i)), 3);

        Assert.Equal(Enumerable.Range(0, 20).ToArray(), results);
        Assert.InRange(probe.Max, 1, 3);
    }

    [Fact]
    public async Task SendAll_WithInvalidLimit_Throws()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            mediator.SendAll(new List<IRequest<int>>(), 0));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            mediator.SendAll<int>(null!, 2));
    }

    // ---- Dead letter queue argument validation ----

    [Fact]
    public void DeadLetterQueue_InvalidMaxEntries_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryDeadLetterQueue(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddInMemoryDeadLetterQueue(-1));
    }
}
