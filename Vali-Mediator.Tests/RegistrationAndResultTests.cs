using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Xunit;

namespace Vali_Mediator.Tests;

public class RegistrationAndResultTests
{
    public record MultiReqA(int V) : IRequest<string>;
    public record MultiReqB(int V) : IRequest<string>;
    public record MultiEvtA : INotification;
    public record MultiEvtB : INotification;

    public class MultiRequestHandler : IRequestHandler<MultiReqA, string>, IRequestHandler<MultiReqB, string>
    {
        public Task<string> Handle(MultiReqA request, CancellationToken ct) => Task.FromResult("a");
        public Task<string> Handle(MultiReqB request, CancellationToken ct) => Task.FromResult("b");
    }

    public class MultiNotificationHandler : INotificationHandler<MultiEvtA>, INotificationHandler<MultiEvtB>
    {
        public static int Calls;
        public Task Handle(MultiEvtA notification, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.CompletedTask; }
        public Task Handle(MultiEvtB notification, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.CompletedTask; }
    }

    public class OpenGenericNotificationHandler<T> : INotificationHandler<MultiEvtA>
    {
        public Task Handle(MultiEvtA notification, CancellationToken ct) => Task.CompletedTask;
    }

    private static ServiceProvider Build(int scans = 1)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(c =>
        {
            for (var i = 0; i < scans; i++) c.RegisterServicesFromAssemblyContaining<RegistrationAndResultTests>();
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ClassImplementingTwoRequestHandlers_RegistersBoth()
    {
        using var sp = Build();
        var m = sp.GetRequiredService<IValiMediator>();
        Assert.Equal("a", await m.Send(new MultiReqA(1)));
        Assert.Equal("b", await m.Send(new MultiReqB(1)));
    }

    [Fact]
    public async Task ClassImplementingTwoNotificationHandlers_RegistersBoth()
    {
        MultiNotificationHandler.Calls = 0;
        using var sp = Build();
        var m = sp.GetRequiredService<IValiMediator>();
        await m.Publish(new MultiEvtA());
        await m.Publish(new MultiEvtB());
        Assert.Equal(2, MultiNotificationHandler.Calls);
    }

    [Fact]
    public async Task ScanningSameAssemblyTwice_DoesNotDuplicateHandlers()
    {
        MultiNotificationHandler.Calls = 0;
        using var sp = Build(scans: 2);
        await sp.GetRequiredService<IValiMediator>().Publish(new MultiEvtA());
        Assert.Equal(1, MultiNotificationHandler.Calls);
    }

    [Fact]
    public void OpenGenericHandlers_AreSkippedByScan()
    {
        using var sp = Build();
        var handlers = sp.GetServices<INotificationHandler<MultiEvtA>>();
        Assert.DoesNotContain(handlers, h => h.GetType().IsGenericType);
    }

    [Fact]
    public void FailWithValidationDictionary_RespectsErrorType()
    {
        var r = Result<int>.Fail(new Dictionary<string, List<string>> { ["x"] = new List<string> { "bad" } }, ErrorType.Conflict);
        Assert.Equal(ErrorType.Conflict, r.ErrorType);
        Assert.Single(r.ValidationErrors!);
    }

    [Fact]
    public void FailWithNullValidationDictionary_Throws()
        => Assert.Throws<ArgumentNullException>(() => Result<int>.Fail((Dictionary<string, List<string>>)null!));

    [Fact]
    public void DefaultResultOfT_IsFailureWithNonNullError()
    {
        var r = default(Result<int>);
        Assert.True(r.IsFailure);
        Assert.NotNull(r.Error);
        Assert.NotEqual(ErrorType.None, r.ErrorType);
        var msg = r.Match(_ => "ok", (e, _) => e);
        Assert.NotNull(msg);
    }

    [Fact]
    public void DefaultResultVoid_IsFailureWithNonNullError()
    {
        var r = default(Result);
        Assert.True(r.IsFailure);
        Assert.NotNull(r.Error);
        Assert.NotEqual(ErrorType.None, r.ErrorType);
    }
}
