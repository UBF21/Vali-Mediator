using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Exceptions;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Processors;
using Vali_Mediator.Core.Request;
using Xunit;

namespace Vali_Mediator.Tests;

public class PipelineTests
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

    private record SimpleRequest(string Value) : IRequest<string>;

    private class SimpleHandler : IRequestHandler<SimpleRequest, string>
    {
        public Task<string> Handle(SimpleRequest request, CancellationToken cancellationToken)
            => Task.FromResult($"echo:{request.Value}");
    }

    private record VoidRequest(string Tag) : IRequest;

    private class VoidRequestHandler : IRequestHandler<VoidRequest>
    {
        public Task<Unit> Handle(VoidRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Unit.Value);
    }

    private record UnknownRequest : IRequest<string>;

    // Behavior that appends to a shared list
    private class TrackingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly ExecutionLog _log;
        private readonly string _name;

        public TrackingBehavior(ExecutionLog log, string name) { _log = log; _name = name; }

        public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
        {
            _log.Entries.Add($"before:{_name}");
            var r = await next();
            _log.Entries.Add($"after:{_name}");
            return r;
        }
    }

    private class ExecutionLog
    {
        public List<string> Entries { get; } = new();
    }

    // Open-generic behaviors for DI registration
    private class OuterBehavior<TReq, TRes> : IPipelineBehavior<TReq, TRes>
        where TReq : IRequest<TRes>
    {
        private readonly ExecutionLog _log;
        public OuterBehavior(ExecutionLog log) => _log = log;

        public async Task<TRes> Handle(TReq request, Func<Task<TRes>> next, CancellationToken cancellationToken)
        {
            _log.Entries.Add("outer-before");
            var r = await next();
            _log.Entries.Add("outer-after");
            return r;
        }
    }

    private class InnerBehavior<TReq, TRes> : IPipelineBehavior<TReq, TRes>
        where TReq : IRequest<TRes>
    {
        private readonly ExecutionLog _log;
        public InnerBehavior(ExecutionLog log) => _log = log;

        public async Task<TRes> Handle(TReq request, Func<Task<TRes>> next, CancellationToken cancellationToken)
        {
            _log.Entries.Add("inner-before");
            var r = await next();
            _log.Entries.Add("inner-after");
            return r;
        }
    }

    // Pre/Post processors
    private record ProcessedRequest(string Value) : IRequest<string>;

    private class ProcessedHandler : IRequestHandler<ProcessedRequest, string>
    {
        private readonly ExecutionLog _log;
        public ProcessedHandler(ExecutionLog log) => _log = log;

        public Task<string> Handle(ProcessedRequest request, CancellationToken cancellationToken)
        {
            _log.Entries.Add("handler");
            return Task.FromResult(request.Value);
        }
    }

    private class MyPreProcessor : IPreProcessor<ProcessedRequest, string>
    {
        private readonly ExecutionLog _log;
        public MyPreProcessor(ExecutionLog log) => _log = log;

        public Task Process(ProcessedRequest request, CancellationToken cancellationToken)
        {
            _log.Entries.Add("pre");
            return Task.CompletedTask;
        }
    }

    private class MyPostProcessor : IPostProcessor<ProcessedRequest, string>
    {
        private readonly ExecutionLog _log;
        public MyPostProcessor(ExecutionLog log) => _log = log;

        public Task Process(ProcessedRequest request, string response, CancellationToken cancellationToken)
        {
            _log.Entries.Add("post");
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Send_WithHandler_ReturnsResponse()
    {
        await using var sp = BuildProvider(s =>
            s.AddTransient<IRequestHandler<SimpleRequest, string>, SimpleHandler>());

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        var response = await mediator.Send(new SimpleRequest("world"));
        Assert.Equal("echo:world", response);
    }

    [Fact]
    public async Task Send_WithoutHandler_ThrowsHandlerNotFoundException()
    {
        await using var sp = BuildProvider();

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<HandlerNotFoundException>(
            () => mediator.Send(new UnknownRequest()));
    }

    [Fact]
    public async Task Send_WithUnitRequest_ReturnsUnit()
    {
        await using var sp = BuildProvider(s =>
            s.AddTransient<IRequestHandler<VoidRequest, Unit>, VoidRequestHandler>());

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        var result = await mediator.Send(new VoidRequest("tag"));
        Assert.Equal(Unit.Value, result);
    }

    [Fact]
    public async Task Send_WithRequestHandlerShorthand_ReturnsUnit()
    {
        // IRequestHandler<VoidRequest> shorthand — registered against IRequestHandler<VoidRequest, Unit>
        await using var sp = BuildProvider(s =>
        {
            // register via the shorthand interface; DI resolves IRequestHandler<VoidRequest, Unit>
            s.AddTransient<IRequestHandler<VoidRequest, Unit>, VoidRequestHandler>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        var result = await mediator.Send(new VoidRequest("shorthand"));
        Assert.Equal(Unit.Value, result);
    }

    [Fact]
    public async Task SendOrDefault_WithHandler_ReturnsResponse()
    {
        await using var sp = BuildProvider(s =>
            s.AddTransient<IRequestHandler<SimpleRequest, string>, SimpleHandler>());

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        var response = await mediator.SendOrDefault(new SimpleRequest("hi"));
        Assert.Equal("echo:hi", response);
    }

    [Fact]
    public async Task SendOrDefault_WithoutHandler_ReturnsDefault()
    {
        await using var sp = BuildProvider();

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        var response = await mediator.SendOrDefault(new UnknownRequest());
        Assert.Null(response);
    }

    [Fact]
    public async Task Send_WithBehavior_BehaviorExecutes()
    {
        var log = new ExecutionLog();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<IRequestHandler<SimpleRequest, string>, SimpleHandler>();
            s.AddTransient<IPipelineBehavior<SimpleRequest, string>, OuterBehavior<SimpleRequest, string>>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await mediator.Send(new SimpleRequest("x"));

        Assert.Contains("outer-before", log.Entries);
        Assert.Contains("outer-after", log.Entries);
    }

    [Fact]
    public async Task Send_WithMultipleBehaviors_ExecutesOuterToInner()
    {
        var log = new ExecutionLog();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<IRequestHandler<SimpleRequest, string>, SimpleHandler>();
            // Register outer first, then inner — outer should wrap inner
            s.AddTransient<IPipelineBehavior<SimpleRequest, string>, OuterBehavior<SimpleRequest, string>>();
            s.AddTransient<IPipelineBehavior<SimpleRequest, string>, InnerBehavior<SimpleRequest, string>>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await mediator.Send(new SimpleRequest("y"));

        var entries = log.Entries;
        Assert.Equal("outer-before", entries[0]);
        Assert.Equal("inner-before", entries[1]);
        Assert.Equal("inner-after", entries[2]);
        Assert.Equal("outer-after", entries[3]);
    }

    [Fact]
    public async Task Send_WithPreAndPostProcessors_ExecutesInOrder()
    {
        var log = new ExecutionLog();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<IRequestHandler<ProcessedRequest, string>, ProcessedHandler>();
            s.AddTransient<IPreProcessor<ProcessedRequest, string>, MyPreProcessor>();
            s.AddTransient<IPostProcessor<ProcessedRequest, string>, MyPostProcessor>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await mediator.Send(new ProcessedRequest("val"));

        Assert.Equal(new[] { "pre", "handler", "post" }, log.Entries);
    }

    [Fact]
    public async Task Send_RequestIsNull_ThrowsArgumentNullException()
    {
        await using var sp = BuildProvider();

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => mediator.Send((IRequest<string>)null!));
    }
}
