using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Streaming;

namespace Vali_Mediator.Benchmarks;

// -------------------------------------------------------------------------
// Domain types used by benchmarks
// -------------------------------------------------------------------------

internal record PingRequest(string Message) : IRequest<string>;

internal class PingHandler : IRequestHandler<PingRequest, string>
{
    public Task<string> Handle(PingRequest request, CancellationToken cancellationToken)
        => Task.FromResult(request.Message);
}

internal class NoBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
        => next();
}

internal record BenchmarkEvent : INotification;

internal class Handler1 : INotificationHandler<BenchmarkEvent>
{
    public Task Handle(BenchmarkEvent n, CancellationToken ct) => Task.CompletedTask;
}

internal class Handler2 : INotificationHandler<BenchmarkEvent>
{
    public Task Handle(BenchmarkEvent n, CancellationToken ct) => Task.CompletedTask;
}

internal class Handler3 : INotificationHandler<BenchmarkEvent>
{
    public Task Handle(BenchmarkEvent n, CancellationToken ct) => Task.CompletedTask;
}

internal record StreamRequest(int Count) : IStreamRequest<int>;

internal class StreamHandler : IStreamRequestHandler<StreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(StreamRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < request.Count; i++)
        {
            yield return i;
            await Task.Yield();
        }
    }
}

// -------------------------------------------------------------------------
// Benchmark classes
// -------------------------------------------------------------------------

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class RequestBenchmarks
{
    private ServiceProvider _spNoBehaviors = null!;
    private ServiceProvider _spOneBehavior = null!;
    private ServiceProvider _spThreeBehaviors = null!;

    [GlobalSetup]
    public void Setup()
    {
        _spNoBehaviors = BuildSp(0);
        _spOneBehavior = BuildSp(1);
        _spThreeBehaviors = BuildSp(3);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _spNoBehaviors.Dispose();
        _spOneBehavior.Dispose();
        _spThreeBehaviors.Dispose();
    }

    private static ServiceProvider BuildSp(int behaviorCount)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        services.AddTransient<IRequestHandler<PingRequest, string>, PingHandler>();
        for (int i = 0; i < behaviorCount; i++)
            services.AddTransient<IPipelineBehavior<PingRequest, string>, NoBehavior<PingRequest, string>>();
        return services.BuildServiceProvider();
    }

    [Benchmark(Baseline = true)]
    public async Task<string> Send_NoBehaviors()
    {
        using var scope = _spNoBehaviors.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();
        return await mediator.Send(new PingRequest("hello"));
    }

    [Benchmark]
    public async Task<string> Send_WithOneBehavior()
    {
        using var scope = _spOneBehavior.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();
        return await mediator.Send(new PingRequest("hello"));
    }

    [Benchmark]
    public async Task<string> Send_WithThreeBehaviors()
    {
        using var scope = _spThreeBehaviors.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();
        return await mediator.Send(new PingRequest("hello"));
    }
}

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class NotificationBenchmarks
{
    private ServiceProvider _spSequential = null!;
    private ServiceProvider _spParallel = null!;
    private ServiceProvider _spResilientParallel = null!;

    [GlobalSetup]
    public void Setup()
    {
        _spSequential = BuildSp();
        _spParallel = BuildSp();
        _spResilientParallel = BuildSp();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _spSequential.Dispose();
        _spParallel.Dispose();
        _spResilientParallel.Dispose();
    }

    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        services.AddTransient<INotificationHandler<BenchmarkEvent>, Handler1>();
        services.AddTransient<INotificationHandler<BenchmarkEvent>, Handler2>();
        services.AddTransient<INotificationHandler<BenchmarkEvent>, Handler3>();
        return services.BuildServiceProvider();
    }

    [Benchmark(Baseline = true)]
    public async Task Publish_Sequential_ThreeHandlers()
    {
        using var scope = _spSequential.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();
        await mediator.Publish(new BenchmarkEvent(), PublishStrategy.Sequential);
    }

    [Benchmark]
    public async Task Publish_Parallel_ThreeHandlers()
    {
        using var scope = _spParallel.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();
        await mediator.Publish(new BenchmarkEvent(), PublishStrategy.Parallel);
    }

    [Benchmark]
    public async Task Publish_ResilientParallel_ThreeHandlers()
    {
        using var scope = _spResilientParallel.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();
        await mediator.Publish(new BenchmarkEvent(), PublishStrategy.ResilientParallel);
    }
}

[MemoryDiagnoser]
public class StreamingBenchmarks
{
    private ServiceProvider _sp = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        services.AddTransient<IStreamRequestHandler<StreamRequest, int>, StreamHandler>();
        _sp = services.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup() => _sp.Dispose();

    [Benchmark]
    public async Task<int> CreateStream_TenElements()
    {
        using var scope = _sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();
        var sum = 0;
        await foreach (var item in mediator.CreateStream(new StreamRequest(10)))
            sum += item;
        return sum;
    }
}
