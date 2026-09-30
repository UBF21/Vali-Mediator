using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Processors;
using Vali_Mediator.Core.Request;
using Xunit;

namespace Vali_Mediator.Tests;

public class ProcessorAndConfigurationTests
{
    private sealed class Log
    {
        public List<string> Entries { get; } = new();
    }

    private static ServiceProvider Build(
        Action<ValiMediatorConfiguration> configure, Log log, Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddValiMediator(configure);
        register?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // ---- notification processors ----

    private record Note : INotification;

    private sealed class NoteHandler : INotificationHandler<Note>
    {
        private readonly Log _log;
        public NoteHandler(Log log) => _log = log;

        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            _log.Entries.Add("handler");
            return Task.CompletedTask;
        }
    }

    private sealed class NotePre : IPreProcessor<Note>
    {
        private readonly Log _log;
        public NotePre(Log log) => _log = log;

        public Task Process(Note dispatch, CancellationToken cancellationToken)
        {
            _log.Entries.Add("pre");
            return Task.CompletedTask;
        }
    }

    private sealed class NotePost : IPostProcessor<Note>
    {
        private readonly Log _log;
        public NotePost(Log log) => _log = log;

        public Task Process(Note dispatch, CancellationToken cancellationToken)
        {
            _log.Entries.Add("post");
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(PublishStrategy.Sequential)]
    [InlineData(PublishStrategy.Parallel)]
    [InlineData(PublishStrategy.ResilientParallel)]
    public async Task Publish_RunsExplicitPreAndPostProcessorsAroundTheHandlers(PublishStrategy strategy)
    {
        var log = new Log();
        using var provider = Build(c => c
            .AddPreProcessor(typeof(IPreProcessor<Note>), typeof(NotePre))
            .AddPostProcessor(typeof(IPostProcessor<Note>), typeof(NotePost)), log,
            s => s.AddScoped<INotificationHandler<Note>, NoteHandler>());

        await provider.GetRequiredService<IValiMediator>().Publish(new Note(), strategy);

        Assert.Equal(new[] { "pre", "handler", "post" }, log.Entries);
    }

    // ---- fire and forget processors ----

    private record Fire : IFireAndForget;

    private sealed class FireHandler : IFireAndForgetHandler<Fire>
    {
        private readonly Log _log;
        public FireHandler(Log log) => _log = log;

        public Task Handle(Fire fireAndForget, CancellationToken cancellationToken)
        {
            _log.Entries.Add("fire-handler");
            return Task.CompletedTask;
        }
    }

    private sealed class FirePre : IPreProcessor<Fire>
    {
        private readonly Log _log;
        public FirePre(Log log) => _log = log;

        public Task Process(Fire dispatch, CancellationToken cancellationToken)
        {
            _log.Entries.Add("fire-pre");
            return Task.CompletedTask;
        }
    }

    private sealed class FirePost : IPostProcessor<Fire>
    {
        private readonly Log _log;
        public FirePost(Log log) => _log = log;

        public Task Process(Fire dispatch, CancellationToken cancellationToken)
        {
            _log.Entries.Add("fire-post");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FireAndForget_RunsExplicitPreAndPostProcessorsAroundTheHandler()
    {
        var log = new Log();
        using var provider = Build(c => c
            .AddPreProcessor(typeof(IPreProcessor<Fire>), typeof(FirePre))
            .AddPostProcessor(typeof(IPostProcessor<Fire>), typeof(FirePost)), log,
            s => s.AddScoped<IFireAndForgetHandler<Fire>, FireHandler>());

        await provider.GetRequiredService<IValiMediator>().Send(new Fire());

        Assert.Equal(new[] { "fire-pre", "fire-handler", "fire-post" }, log.Entries);
    }

    // ---- request processors ----

    private record Ask(int Value) : IRequest<int>;

    private sealed class AskHandler : IRequestHandler<Ask, int>
    {
        private readonly Log _log;
        public AskHandler(Log log) => _log = log;

        public Task<int> Handle(Ask request, CancellationToken cancellationToken)
        {
            _log.Entries.Add("ask-handler");
            return Task.FromResult(request.Value + 1);
        }
    }

    private sealed class AskPre : IPreProcessor<Ask, int>
    {
        private readonly Log _log;
        public AskPre(Log log) => _log = log;

        public Task Process(Ask request, CancellationToken cancellationToken)
        {
            _log.Entries.Add("ask-pre");
            return Task.CompletedTask;
        }
    }

    private sealed class AskPost : IPostProcessor<Ask, int>
    {
        private readonly Log _log;
        public AskPost(Log log) => _log = log;

        public Task Process(Ask request, int response, CancellationToken cancellationToken)
        {
            _log.Entries.Add("ask-post:" + response);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Send_RunsExplicitRequestPreAndPostProcessors()
    {
        var log = new Log();
        using var provider = Build(c => c
            .AddRequestPreProcessor(typeof(IPreProcessor<Ask, int>), typeof(AskPre))
            .AddRequestPostProcessor(typeof(IPostProcessor<Ask, int>), typeof(AskPost)), log,
            s => s.AddScoped<IRequestHandler<Ask, int>, AskHandler>());

        var result = await provider.GetRequiredService<IValiMediator>().Send(new Ask(4));

        Assert.Equal(5, result);
        Assert.Equal(new[] { "ask-pre", "ask-handler", "ask-post:5" }, log.Entries);
    }

    // ---- argument validation ----

    private static readonly ValiMediatorConfiguration Config = new();

    [Fact]
    public void AddBehavior_RejectsNullAndNonBehaviorInterfaces()
    {
        Assert.Throws<ArgumentNullException>(() => Config.AddBehavior(null!, typeof(NotePre)));
        Assert.Throws<ArgumentNullException>(() => Config.AddBehavior(typeof(IPipelineBehavior<,>), null!));
        Assert.Throws<ArgumentException>(() => Config.AddBehavior(typeof(IDisposable), typeof(NotePre)));
        Assert.Throws<ArgumentException>(() => Config.AddBehavior(typeof(IEquatable<>), typeof(NotePre)));
    }

    [Fact]
    public void AddPreProcessor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Config.AddPreProcessor(null!, typeof(NotePre)));
        Assert.Throws<ArgumentNullException>(() => Config.AddPreProcessor(typeof(IPreProcessor<Note>), null!));
        Assert.Throws<ArgumentException>(() => Config.AddPreProcessor(typeof(IDisposable), typeof(NotePre)));
        Assert.Throws<ArgumentException>(() => Config.AddPreProcessor(typeof(IPreProcessor<Ask, int>), typeof(AskPre)));
    }

    [Fact]
    public void AddPostProcessor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Config.AddPostProcessor(null!, typeof(NotePost)));
        Assert.Throws<ArgumentNullException>(() => Config.AddPostProcessor(typeof(IPostProcessor<Note>), null!));
        Assert.Throws<ArgumentException>(() => Config.AddPostProcessor(typeof(IDisposable), typeof(NotePost)));
        Assert.Throws<ArgumentException>(() => Config.AddPostProcessor(typeof(IPostProcessor<Ask, int>), typeof(AskPost)));
    }

    [Fact]
    public void AddRequestPreProcessor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Config.AddRequestPreProcessor(null!, typeof(AskPre)));
        Assert.Throws<ArgumentNullException>(() => Config.AddRequestPreProcessor(typeof(IPreProcessor<Ask, int>), null!));
        Assert.Throws<ArgumentException>(() => Config.AddRequestPreProcessor(typeof(IDisposable), typeof(AskPre)));
        Assert.Throws<ArgumentException>(() => Config.AddRequestPreProcessor(typeof(IPreProcessor<Note>), typeof(NotePre)));
    }

    [Fact]
    public void AddRequestPostProcessor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Config.AddRequestPostProcessor(null!, typeof(AskPost)));
        Assert.Throws<ArgumentNullException>(() => Config.AddRequestPostProcessor(typeof(IPostProcessor<Ask, int>), null!));
        Assert.Throws<ArgumentException>(() => Config.AddRequestPostProcessor(typeof(IDisposable), typeof(AskPost)));
        Assert.Throws<ArgumentException>(() => Config.AddRequestPostProcessor(typeof(IPostProcessor<Note>), typeof(NotePost)));
    }

    [Fact]
    public void RegisterServicesFromAssembly_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ValiMediatorConfiguration().RegisterServicesFromAssembly(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void SendAllMaxDegreeOfParallelism_RejectsValuesBelowOne(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ValiMediatorConfiguration { SendAllMaxDegreeOfParallelism = value });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(64)]
    public void SendAllMaxDegreeOfParallelism_AcceptsNullAndPositiveValues(int? value)
    {
        var config = new ValiMediatorConfiguration { SendAllMaxDegreeOfParallelism = value };

        Assert.Equal(value, config.SendAllMaxDegreeOfParallelism);
    }

    // ---- timeout behavior ----

    private record Slow(TimeSpan Timeout) : IRequest<int>, IHasTimeout;

    private sealed class SlowHandler : IRequestHandler<Slow, int>
    {
        public async Task<int> Handle(Slow request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 1;
        }
    }

    [Fact]
    public async Task AddTimeoutBehavior_TimesOutRequestsThatDeclareATimeout()
    {
        var log = new Log();
        using var provider = Build(c => c
            .RegisterServicesFromAssemblyContaining<SlowHandler>()
            .AddTimeoutBehavior(), log);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            provider.GetRequiredService<IValiMediator>().Send(new Slow(TimeSpan.FromMilliseconds(50))));
    }

    [Fact]
    public async Task AddTimeoutBehavior_PassesThroughRequestsWithoutATimeout()
    {
        var log = new Log();
        using var provider = Build(c => c
            .RegisterServicesFromAssemblyContaining<AskHandler>()
            .AddTimeoutBehavior(), log);

        var result = await provider.GetRequiredService<IValiMediator>().Send(new Ask(9));

        Assert.Equal(10, result);
    }
}
