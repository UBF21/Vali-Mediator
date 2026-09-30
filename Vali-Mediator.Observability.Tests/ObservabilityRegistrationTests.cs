using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator_Observability.Core.Abstractions;
using Vali_Mediator_Observability.Core.Context;
using Vali_Mediator_Observability.Core.Metrics;
using Vali_Mediator_Observability.Extension;
using Vali_Mediator_Observability.Observers;
using Vali_Mediator_Observability.Pipeline;
using Xunit;

namespace Vali_Mediator_Observability.Tests;

internal sealed class CustomMetrics : IMetricsCollector
{
    public void RecordRequestStarted(string requestName) { }
    public void RecordRequestCompleted(string requestName, TimeSpan duration, bool success) { }
    public void RecordRequestFailed(string requestName, TimeSpan duration, string exceptionType) { }
}

internal sealed class OtherMetrics : IMetricsCollector
{
    public void RecordRequestStarted(string requestName) { }
    public void RecordRequestCompleted(string requestName, TimeSpan duration, bool success) { }
    public void RecordRequestFailed(string requestName, TimeSpan duration, string exceptionType) { }
}

internal sealed class FirstObserver : IRequestObserver
{
    public Task OnStarted(ObservabilityContext context, CancellationToken ct = default) => Task.CompletedTask;
    public Task OnCompleted(ObservabilityContext context, CancellationToken ct = default) => Task.CompletedTask;
    public Task OnFailed(ObservabilityContext context, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class SecondObserver : IRequestObserver
{
    public List<string> Calls { get; } = new List<string>();

    public Task OnStarted(ObservabilityContext context, CancellationToken ct = default)
    {
        Calls.Add("started:" + context.RequestName);
        return Task.CompletedTask;
    }

    public Task OnCompleted(ObservabilityContext context, CancellationToken ct = default)
    {
        Calls.Add("completed:" + context.RequestName);
        return Task.CompletedTask;
    }

    public Task OnFailed(ObservabilityContext context, CancellationToken ct = default) => Task.CompletedTask;
}

public class ObservabilityRegistrationTests
{
    private static ServiceDescriptor Single(IServiceCollection services, Type serviceType)
        => Assert.Single(services, d => d.ServiceType == serviceType);

    [Fact]
    public void AddObservability_RegistersNoOpCollectorAndReturnsSameCollection()
    {
        var services = new ServiceCollection();

        var returned = services.AddObservability();

        Assert.Same(services, returned);
        var descriptor = Single(services, typeof(IMetricsCollector));
        Assert.Equal(typeof(NoOpMetricsCollector), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddObservabilityBehavior_RegistersBothOpenGenericBehaviorsAsTransientByDefault()
    {
        var services = new ServiceCollection();
        services.AddObservability();
        services.AddValiMediator(config => config.AddObservabilityBehavior());

        var request = Assert.Single(services, d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(ObservabilityBehavior<,>));
        var dispatch = Assert.Single(services, d =>
            d.ServiceType == typeof(IPipelineBehavior<>) && d.ImplementationType == typeof(ObservabilityDispatchBehavior<>));
        Assert.Equal(ServiceLifetime.Transient, request.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, dispatch.Lifetime);
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void AddObservabilityBehavior_RespectsRequestedLifetime(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(config => config.AddObservabilityBehavior(lifetime));

        Assert.All(
            services.Where(d => d.ImplementationType == typeof(ObservabilityBehavior<,>)
                                || d.ImplementationType == typeof(ObservabilityDispatchBehavior<>)),
            d => Assert.Equal(lifetime, d.Lifetime));
    }

    [Fact]
    public void AddObservabilityBehavior_ReturnsSameConfigurationForChaining()
    {
        ValiMediatorConfiguration? captured = null;
        ValiMediatorConfiguration? returned = null;

        new ServiceCollection().AddValiMediator(config =>
        {
            captured = config;
            returned = config.AddObservabilityBehavior();
        });

        Assert.NotNull(captured);
        Assert.Same(captured, returned);
    }

    [Fact]
    public async Task RegisteredBehaviors_ResolveFromContainerAndReachTheObservers()
    {
        var observer = new SecondObserver();
        var services = new ServiceCollection();
        services.AddObservability();
        services.AddSingleton<IRequestObserver>(observer);
        services.AddValiMediator(config => config.AddObservabilityBehavior());
        using var provider = services.BuildServiceProvider();

        var requestBehavior = provider.GetRequiredService<IPipelineBehavior<TelemetryRequest, string>>();
        var dispatchBehavior = provider.GetRequiredService<IPipelineBehavior<TelemetryNotification>>();

        Assert.IsType<ObservabilityBehavior<TelemetryRequest, string>>(requestBehavior);
        Assert.IsType<ObservabilityDispatchBehavior<TelemetryNotification>>(dispatchBehavior);

        var response = await requestBehavior.Handle(
            new TelemetryRequest(), _ => Task.FromResult("done"), CancellationToken.None);
        await dispatchBehavior.Handle(new TelemetryNotification(), _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal("done", response);
        Assert.Equal(new[] { "started:TelemetryRequest", "completed:TelemetryRequest" }, observer.Calls);
    }

    [Fact]
    public void AddMetricsCollector_ReplacesTheDefaultCollector()
    {
        var services = new ServiceCollection();
        services.AddObservability();

        var returned = services.AddMetricsCollector<CustomMetrics>();

        Assert.Same(services, returned);
        var descriptor = Single(services, typeof(IMetricsCollector));
        Assert.Equal(typeof(CustomMetrics), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddMetricsCollector_CalledTwice_KeepsOnlyTheLastOne()
    {
        var services = new ServiceCollection();
        services.AddMetricsCollector<CustomMetrics>();
        services.AddMetricsCollector<OtherMetrics>(ServiceLifetime.Scoped);

        var descriptor = Single(services, typeof(IMetricsCollector));
        Assert.Equal(typeof(OtherMetrics), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddMetricsCollector_WithoutPriorRegistration_JustAdds()
    {
        var services = new ServiceCollection();

        services.AddMetricsCollector<CustomMetrics>();

        Assert.Equal(typeof(CustomMetrics), Single(services, typeof(IMetricsCollector)).ImplementationType);
    }

    [Fact]
    public void AddMetricsCollector_LeavesUnrelatedRegistrationsUntouched()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestObserver, FirstObserver>();
        services.AddObservability();

        services.AddMetricsCollector<CustomMetrics>();

        Assert.Single(services, d => d.ServiceType == typeof(IRequestObserver));
    }

    [Fact]
    public void AddConsoleMetrics_ResolvesTheConsoleCollector()
    {
        var services = new ServiceCollection();
        services.AddObservability();
        services.AddConsoleMetrics();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<ConsoleMetricsCollector>(provider.GetRequiredService<IMetricsCollector>());
    }

    [Fact]
    public void AddRequestObserver_DefaultsToTransientAndAccumulatesObservers()
    {
        var services = new ServiceCollection();

        var returned = services.AddRequestObserver<FirstObserver>().AddRequestObserver<SecondObserver>();

        Assert.Same(services, returned);
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IRequestObserver)));
        Assert.All(services, d => Assert.Equal(ServiceLifetime.Transient, d.Lifetime));

        using var provider = services.BuildServiceProvider();
        var observers = provider.GetServices<IRequestObserver>().ToList();
        Assert.IsType<FirstObserver>(observers[0]);
        Assert.IsType<SecondObserver>(observers[1]);
    }

    [Fact]
    public void AddRequestObserver_RespectsRequestedLifetime()
    {
        var services = new ServiceCollection();

        services.AddRequestObserver<FirstObserver>(ServiceLifetime.Singleton);

        Assert.Equal(ServiceLifetime.Singleton, Single(services, typeof(IRequestObserver)).Lifetime);
    }

    [Fact]
    public void AddConsoleLoggingObserver_RegistersTheObserverAsTransient()
    {
        var services = new ServiceCollection();
        services.AddObservability(o => o.IncludeExceptionMessage = true);

        services.AddConsoleLoggingObserver();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<ConsoleLoggingObserver>(Assert.Single(provider.GetServices<IRequestObserver>()));
        Assert.Equal(ServiceLifetime.Transient, Single(services, typeof(IRequestObserver)).Lifetime);
    }
}
