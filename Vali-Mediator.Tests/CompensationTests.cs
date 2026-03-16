using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.Compensation;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Xunit;

namespace Vali_Mediator.Tests;

public class CompensationTests
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

    private class RollbackCommand : IFireAndForget
    {
        public string OperationId { get; init; } = string.Empty;
    }

    private class RollbackHandler : IFireAndForgetHandler<RollbackCommand>
    {
        private readonly List<string> _log;
        public RollbackHandler(List<string> log) => _log = log;

        public Task Handle(RollbackCommand command, CancellationToken cancellationToken)
        {
            _log.Add($"rollback:{command.OperationId}");
            return Task.CompletedTask;
        }
    }

    private class CompensableOperation : Compensable
    {
        public string Id { get; init; } = string.Empty;

        public override IFireAndForget? GetCompensation()
            => new RollbackCommand { OperationId = Id };
    }

    private class NoCompensationOperation : Compensable
    {
        public override IFireAndForget? GetCompensation() => null;
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Compensate_WhenCompensationExists_ExecutesFireAndForget()
    {
        var log = new List<string>();

        await using var sp = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddTransient<IFireAndForgetHandler<RollbackCommand>, RollbackHandler>();
        });

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        var operation = new CompensableOperation { Id = "op-123" };
        await operation.Compensate(mediator);

        Assert.Contains("rollback:op-123", log);
    }

    [Fact]
    public async Task Compensate_WhenCompensationIsNull_CompletesWithoutDispatch()
    {
        await using var sp = BuildProvider();

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        var operation = new NoCompensationOperation();

        // Should not throw even though no handlers are registered
        await operation.Compensate(mediator);
    }
}
