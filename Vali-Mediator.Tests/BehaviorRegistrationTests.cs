using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Xunit;

namespace Vali_Mediator.Tests;

public class BehaviorRegistrationTests
{
    private sealed class Log { public List<string> Entries { get; } = new(); }

    private record Ping(string V) : IRequest<string>;

    private sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult(request.V);
    }

    private sealed class ClosedA : IPipelineBehavior<Ping, string>
    {
        private readonly Log _log;
        public ClosedA(Log log) => _log = log;
        public async Task<string> Handle(Ping request, Func<CancellationToken, Task<string>> next, CancellationToken cancellationToken)
        {
            _log.Entries.Add("A>");
            var r = await next(cancellationToken);
            _log.Entries.Add("<A");
            return r;
        }
    }

    private sealed class ClosedB : IPipelineBehavior<Ping, string>
    {
        private readonly Log _log;
        public ClosedB(Log log) => _log = log;
        public async Task<string> Handle(Ping request, Func<CancellationToken, Task<string>> next, CancellationToken cancellationToken)
        {
            _log.Entries.Add("B>");
            var r = await next(cancellationToken);
            _log.Entries.Add("<B");
            return r;
        }
    }

    private sealed class OpenC<TReq, TRes> : IPipelineBehavior<TReq, TRes> where TReq : IRequest<TRes>
    {
        private readonly Log _log;
        public OpenC(Log log) => _log = log;
        public async Task<TRes> Handle(TReq request, Func<CancellationToken, Task<TRes>> next, CancellationToken cancellationToken)
        {
            _log.Entries.Add("C>");
            var r = await next(cancellationToken);
            _log.Entries.Add("<C");
            return r;
        }
    }

    private record Note(string V) : INotification;

    private sealed class NoteHandler : INotificationHandler<Note>
    {
        private readonly Log _log;
        public NoteHandler(Log log) => _log = log;
        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            _log.Entries.Add("H");
            return Task.CompletedTask;
        }
    }

    private sealed class ClosedDispatch : IPipelineBehavior<Note>
    {
        private readonly Log _log;
        public ClosedDispatch(Log log) => _log = log;
        public async Task Handle(Note dispatch, Func<CancellationToken, Task> next, CancellationToken cancellationToken)
        {
            _log.Entries.Add("D>");
            await next(cancellationToken);
            _log.Entries.Add("<D");
        }
    }

    private sealed class NotABehavior { }

    private static ServiceProvider Build(Action<ValiMediatorConfiguration> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Log>();
        services.AddValiMediator(c =>
        {
            c.RegisterServicesFromAssemblyContaining<BehaviorRegistrationTests>();
            configure(c);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ClosedRequestBehaviors_RunInRegistrationOrder_FirstIsOutermost()
    {
        using var sp = Build(c => c.AddRequestBehavior<ClosedA>().AddRequestBehavior<ClosedB>());
        var result = await sp.GetRequiredService<IValiMediator>().Send(new Ping("x"));

        Assert.Equal("x", result);
        Assert.Equal(new[] { "A>", "B>", "<B", "<A" }, sp.GetRequiredService<Log>().Entries);
    }

    [Fact]
    public async Task OpenAndClosedBehaviors_CanBeMixed_ViaTypeOverload()
    {
        using var sp = Build(c => c.AddRequestBehavior(typeof(OpenC<,>)).AddRequestBehavior(typeof(ClosedA)));
        await sp.GetRequiredService<IValiMediator>().Send(new Ping("x"));

        Assert.Equal(new[] { "C>", "A>", "<A", "<C" }, sp.GetRequiredService<Log>().Entries);
    }

    [Fact]
    public async Task ClosedDispatchBehavior_WrapsNotificationHandlers()
    {
        using var sp = Build(c => c.AddDispatchBehavior<ClosedDispatch>());
        await sp.GetRequiredService<IValiMediator>().Publish(new Note("n"));

        Assert.Equal(new[] { "D>", "H", "<D" }, sp.GetRequiredService<Log>().Entries);
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Transient)]
    [InlineData(ServiceLifetime.Scoped)]
    public void Lifetime_IsRespected(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Log>();
        services.AddValiMediator(c => c.AddRequestBehavior<ClosedA>(lifetime));

        var descriptor = Assert.Single(services, d => d.ImplementationType == typeof(ClosedA));
        Assert.Equal(typeof(IPipelineBehavior<Ping, string>), descriptor.ServiceType);
        Assert.Equal(lifetime, descriptor.Lifetime);
    }

    [Fact]
    public void TypeThatIsNotABehavior_ThrowsClearError()
    {
        var config = new ValiMediatorConfiguration();

        var ex = Assert.Throws<ArgumentException>(() => config.AddRequestBehavior<NotABehavior>());
        Assert.Contains(nameof(NotABehavior), ex.Message);
        Assert.Throws<ArgumentException>(() => config.AddDispatchBehavior<ClosedA>());
    }
}
