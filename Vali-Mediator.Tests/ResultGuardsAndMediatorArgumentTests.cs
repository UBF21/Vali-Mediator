using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Xunit;

namespace Vali_Mediator.Tests;

public class ResultGuardsAndMediatorArgumentTests
{
    private static readonly Result<int> Ok = Result<int>.Ok(3);
    private static readonly Result<int> Failed = Result<int>.Fail("bad", ErrorType.NotFound);

    // ---- Result<T>: null delegates are rejected ----

    [Fact]
    public async Task ResultOfT_RejectsNullDelegates()
    {
        Assert.Throws<ArgumentNullException>(() => Ok.Match<int>(null!, (_, _) => 0));
        Assert.Throws<ArgumentNullException>(() => Ok.Match(_ => 0, null!));
        Assert.Throws<ArgumentNullException>(() => Ok.Map<int>(null!));
        Assert.Throws<ArgumentNullException>(() => Ok.Bind<int>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Ok.MapAsync<int>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Ok.BindAsync<int>(null!));
        Assert.Throws<ArgumentNullException>(() => Ok.Tap(null!));
        Assert.Throws<ArgumentNullException>(() => Ok.OnFailure(null!));
    }

    // ---- Result<T>: async operations follow the success/failure path ----

    [Fact]
    public async Task ResultOfT_MapAsyncAndBindAsync_TransformSuccessAndPropagateFailure()
    {
        var mapped = await Ok.MapAsync(v => Task.FromResult(v * 2));
        var bound = await Ok.BindAsync(v => Task.FromResult(Result<string>.Ok("v" + v)));
        var mapFailed = await Failed.MapAsync(v => Task.FromResult(v * 2));
        var bindFailed = await Failed.BindAsync(v => Task.FromResult(Result<string>.Ok("v" + v)));

        Assert.Equal(6, mapped.Value);
        Assert.Equal("v3", bound.Value);
        Assert.Equal(ErrorType.NotFound, mapFailed.ErrorType);
        Assert.Equal("bad", mapFailed.Error);
        Assert.Equal(ErrorType.NotFound, bindFailed.ErrorType);
    }

    [Fact]
    public void ResultOfT_TapAndOnFailure_RunOnTheMatchingPathOnly()
    {
        var tapped = 0;
        var failures = 0;

        Ok.Tap(_ => tapped++).OnFailure((_, _) => failures++);
        Failed.Tap(_ => tapped++).OnFailure((_, _) => failures++);

        Assert.Equal(1, tapped);
        Assert.Equal(1, failures);
    }

    // ---- Result (void): null delegates are rejected ----

    [Fact]
    public void Result_RejectsNullDelegates()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Ok().Map<int>(null!));
        Assert.Throws<ArgumentNullException>(() => Result.Ok().Match<int>(null!, (_, _) => 0));
        Assert.Throws<ArgumentNullException>(() => Result.Ok().Match(() => 0, null!));
    }

    [Fact]
    public void Result_MapAndMatch_FollowTheSuccessAndFailurePaths()
    {
        Assert.Equal(7, Result.Ok().Map(() => 7).Value);
        Assert.Equal(ErrorType.Conflict, Result.Fail("x", ErrorType.Conflict).Map(() => 7).ErrorType);
        Assert.Equal("ok", Result.Ok().Match(() => "ok", (e, _) => e));
        Assert.Equal("x", Result.Fail("x").Match(() => "ok", (e, _) => e));
    }

    // ---- mediator and registration argument checks ----

    private record Req : IRequest<int>;
    private record Note : INotification;
    private record Fire : IFireAndForget;

    private static IValiMediator Mediator(out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IValiMediator>();
    }

    [Fact]
    public async Task Mediator_RejectsNullMessages()
    {
        var mediator = Mediator(out var provider);
        using (provider)
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Send<int>(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.SendOrDefault<int>(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Publish<Note>(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Send((IFireAndForget)null!));
        }
    }

    [Fact]
    public void AddValiMediator_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddValiMediator(_ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddValiMediator(null!));
    }

    // ---- registration: the same handler type for the base and runtime notification runs once ----

    private record BaseNote : INotification;
    private record DerivedNote : BaseNote;

    private sealed class Counter
    {
        public int Calls;
    }

    private sealed class SharedHandler : INotificationHandler<BaseNote>, INotificationHandler<DerivedNote>
    {
        private readonly Counter _counter;
        public SharedHandler(Counter counter) => _counter = counter;

        Task INotificationHandler<BaseNote>.Handle(BaseNote notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.Calls);
            return Task.CompletedTask;
        }

        Task INotificationHandler<DerivedNote>.Handle(DerivedNote notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _counter.Calls);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publish_ByBaseTypeWithARuntimeSubtype_RunsAHandlerRegisteredForBothOnlyOnce()
    {
        var counter = new Counter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddValiMediator(_ => { });
        services.AddScoped<INotificationHandler<BaseNote>, SharedHandler>();
        services.AddScoped<INotificationHandler<DerivedNote>, SharedHandler>();
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IValiMediator>().Publish<BaseNote>(new DerivedNote());

        Assert.Equal(1, counter.Calls);
    }
}
