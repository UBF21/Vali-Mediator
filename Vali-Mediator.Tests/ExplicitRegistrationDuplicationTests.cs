using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Processors;
using Xunit;

namespace Vali_Mediator.Tests;

public class ExplicitRegistrationDuplicationTests
{
    public record DupNote : INotification;

    public sealed class DupPre : IPreProcessor<DupNote>
    {
        public Task Process(DupNote dispatch, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class DupPost : IPostProcessor<DupNote>
    {
        public Task Process(DupNote dispatch, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static int Count(IServiceCollection services, Type service, Type implementation) =>
        services.Count(d => d.ServiceType == service && d.ImplementationType == implementation);

    [Fact]
    public void ExplicitProcessorAlsoFoundByTheScan_IsRegisteredOnce()
    {
        var services = new ServiceCollection();

        services.AddValiMediator(c => c
            .RegisterServicesFromAssemblyContaining<ExplicitRegistrationDuplicationTests>()
            .AddPreProcessor(typeof(IPreProcessor<DupNote>), typeof(DupPre))
            .AddPostProcessor(typeof(IPostProcessor<DupNote>), typeof(DupPost)));

        Assert.Equal(1, Count(services, typeof(IPreProcessor<DupNote>), typeof(DupPre)));
        Assert.Equal(1, Count(services, typeof(IPostProcessor<DupNote>), typeof(DupPost)));
    }

    [Fact]
    public void ExplicitProcessorWithoutScan_IsStillRegistered()
    {
        var services = new ServiceCollection();

        services.AddValiMediator(c => c
            .AddPreProcessor(typeof(IPreProcessor<DupNote>), typeof(DupPre)));

        Assert.Equal(1, Count(services, typeof(IPreProcessor<DupNote>), typeof(DupPre)));
    }
}
