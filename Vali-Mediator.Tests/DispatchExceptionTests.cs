using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Streaming;
using Xunit;

namespace Vali_Mediator.Tests;

public class DispatchExceptionTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        register(services);
        return services.BuildServiceProvider();
    }

    private record BoomRequest : IRequest<int>;

    private class BoomHandler : IRequestHandler<BoomRequest, int>
    {
        public Task<int> Handle(BoomRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("sync boom");
    }

    private record BoomCommand : IFireAndForget;

    private class BoomCommandHandler : IFireAndForgetHandler<BoomCommand>
    {
        public Task Handle(BoomCommand command, CancellationToken cancellationToken)
            => throw new InvalidOperationException("sync boom");
    }

    private record BoomStream : IStreamRequest<int>;

    private class BoomStreamHandler : IStreamRequestHandler<BoomStream, int>
    {
        public IAsyncEnumerable<int> Handle(BoomStream request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("sync boom");
    }

    [Fact]
    public async Task Send_HandlerThrowsSynchronously_PropagatesOriginalException()
    {
        using var provider = BuildProvider(s => s.AddScoped<IRequestHandler<BoomRequest, int>, BoomHandler>());
        var mediator = provider.GetRequiredService<IValiMediator>();

        var ex = await Record.ExceptionAsync(() => mediator.Send(new BoomRequest()));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public async Task Send_FireAndForgetThrowsSynchronously_PropagatesOriginalException()
    {
        using var provider = BuildProvider(s => s.AddScoped<IFireAndForgetHandler<BoomCommand>, BoomCommandHandler>());
        var mediator = provider.GetRequiredService<IValiMediator>();

        var ex = await Record.ExceptionAsync(() => mediator.Send(new BoomCommand()));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void CreateStream_HandlerThrowsSynchronously_PropagatesOriginalException()
    {
        using var provider = BuildProvider(s => s.AddScoped<IStreamRequestHandler<BoomStream, int>, BoomStreamHandler>());
        var mediator = provider.GetRequiredService<IValiMediator>();

        var ex = Record.Exception(() => mediator.CreateStream(new BoomStream()));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.IsNotType<TargetInvocationException>(ex);
    }

    private record RuntimeEvent : INotification;

    private class RuntimeEventHandler : INotificationHandler<RuntimeEvent>
    {
        public static int Calls;
        public Task Handle(RuntimeEvent notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publish_WithBaseInterfaceStaticType_UsesRuntimeTypeHandlers()
    {
        RuntimeEventHandler.Calls = 0;
        using var provider = BuildProvider(s => s.AddScoped<INotificationHandler<RuntimeEvent>, RuntimeEventHandler>());
        var mediator = provider.GetRequiredService<IValiMediator>();
        INotification notification = new RuntimeEvent();

        await mediator.Publish(notification);

        Assert.Equal(1, RuntimeEventHandler.Calls);
    }
}
