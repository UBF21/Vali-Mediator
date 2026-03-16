using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Exceptions;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Xunit;

namespace Vali_Mediator.Tests;

public class FireAndForgetTests
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

    private class SendEmailCommand : IFireAndForget
    {
        public string Email { get; init; } = string.Empty;
    }

    private class UnknownCommand : IFireAndForget { }

    private class SendEmailHandler : IFireAndForgetHandler<SendEmailCommand>
    {
        private readonly List<string> _log;
        public SendEmailHandler(List<string> log) => _log = log;

        public Task Handle(SendEmailCommand command, CancellationToken cancellationToken)
        {
            _log.Add($"sent:{command.Email}");
            return Task.CompletedTask;
        }
    }

    private class LoggingFireAndForgetBehavior<TRequest> : IPipelineBehavior<TRequest>
        where TRequest : IFireAndForget
    {
        private readonly List<string> _log;
        public LoggingFireAndForgetBehavior(List<string> log) => _log = log;

        public async Task Handle(TRequest request, Func<Task> next, CancellationToken cancellationToken)
        {
            _log.Add("behavior-before");
            await next();
            _log.Add("behavior-after");
        }
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Send_FireAndForget_HandlerExecutes()
    {
        var log = new List<string>();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<IFireAndForgetHandler<SendEmailCommand>, SendEmailHandler>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await mediator.Send(new SendEmailCommand { Email = "test@example.com" });

        Assert.Contains("sent:test@example.com", log);
    }

    [Fact]
    public async Task Send_FireAndForget_WithoutHandler_ThrowsHandlerNotFoundException()
    {
        await using var sp = BuildProvider();

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<HandlerNotFoundException>(
            () => mediator.Send(new UnknownCommand()));
    }

    [Fact]
    public async Task Send_FireAndForget_WithBehavior_BehaviorExecutes()
    {
        var log = new List<string>();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<IFireAndForgetHandler<SendEmailCommand>, SendEmailHandler>();
            s.AddTransient<IPipelineBehavior<SendEmailCommand>, LoggingFireAndForgetBehavior<SendEmailCommand>>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await mediator.Send(new SendEmailCommand { Email = "b@b.com" });

        Assert.Contains("behavior-before", log);
        Assert.Contains("behavior-after", log);
        Assert.Contains("sent:b@b.com", log);

        var beforeIdx = log.IndexOf("behavior-before");
        var sentIdx = log.IndexOf("sent:b@b.com");
        var afterIdx = log.IndexOf("behavior-after");
        Assert.True(beforeIdx < sentIdx && sentIdx < afterIdx);
    }

    [Fact]
    public async Task Send_FireAndForgetIsNull_ThrowsArgumentNullException()
    {
        await using var sp = BuildProvider();

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => mediator.Send((IFireAndForget)null!));
    }
}
