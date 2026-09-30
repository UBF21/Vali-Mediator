using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Policies;
using Vali_Mediator_Resilience.Core.Registry;
using Vali_Mediator_Resilience.Integration;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class ExtensionRegistrationTests
{
    public sealed record Ping(int Id) : IRequest<int>;

    private sealed class PingProvider : IResiliencePolicyProvider<Ping>
    {
        public ResiliencePolicy GetPolicy(Ping request) => ResiliencePolicy.Create("ping").Build();
    }

    [Fact]
    public void AddResilienceBehavior_RegistersTheOpenGenericBehavior()
    {
        var services = new ServiceCollection();
        services.AddValiMediator(config => config.AddResilienceBehavior());

        using var provider = services.BuildServiceProvider();
        var behaviors = provider.GetServices<IPipelineBehavior<Ping, int>>().ToList();

        Assert.Contains(behaviors, b => b is ResilienceBehavior<Ping, int>);
    }

    [Fact]
    public void AddResilienceBehavior_HonoursTheRequestedLifetime()
    {
        var services = new ServiceCollection();
        services.AddValiMediator(config => config.AddResilienceBehavior(ServiceLifetime.Singleton));

        using var provider = services.BuildServiceProvider();
        var first = provider.GetServices<IPipelineBehavior<Ping, int>>().OfType<ResilienceBehavior<Ping, int>>().Single();
        var second = provider.GetServices<IPipelineBehavior<Ping, int>>().OfType<ResilienceBehavior<Ping, int>>().Single();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task AddResiliencePolicy_RunsTheFactoryForEveryRequest()
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddResiliencePolicy<Ping>(request =>
        {
            calls++;
            return ResiliencePolicy.Create("factory-" + request.Id).Build();
        });

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IResiliencePolicyProvider<Ping>>();

        var policy1 = resolved.GetPolicy(new Ping(1));
        var policy2 = resolved.GetPolicy(new Ping(2));

        Assert.Equal(2, calls);
        Assert.NotSame(policy1, policy2);
        Assert.Equal(7, await policy1.ExecuteAsync(() => Task.FromResult(7)));
    }

    [Fact]
    public void AddResiliencePolicy_IsASingleton()
    {
        var services = new ServiceCollection();
        services.AddResiliencePolicy<Ping>(_ => ResiliencePolicy.Create().Build());

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IResiliencePolicyProvider<Ping>));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddResiliencePolicyProvider_UsesScopedByDefault()
    {
        var services = new ServiceCollection();
        services.AddResiliencePolicyProvider<Ping, PingProvider>();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IResiliencePolicyProvider<Ping>));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(PingProvider), descriptor.ImplementationType);
    }

    [Fact]
    public void AddResiliencePolicyProvider_HonoursACustomLifetime()
    {
        var services = new ServiceCollection();
        services.AddResiliencePolicyProvider<Ping, PingProvider>(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IResiliencePolicyProvider<Ping>>(),
            provider.GetRequiredService<IResiliencePolicyProvider<Ping>>());
    }

    [Fact]
    public void AddGlobalResiliencePolicy_WithAPolicyReturnsThatSamePolicyForEveryRequest()
    {
        var policy = ResiliencePolicy.Create("global").Build();
        var services = new ServiceCollection();
        services.AddGlobalResiliencePolicy(policy);

        using var provider = services.BuildServiceProvider();
        var global = provider.GetRequiredService<IGlobalResiliencePolicyProvider>();

        Assert.Same(policy, global.GetPolicy(new Ping(1)));
        Assert.Same(policy, global.GetPolicy("anything"));
    }

    [Fact]
    public void AddGlobalResiliencePolicy_WithAFactoryReceivesTheRequest()
    {
        object? seen = null;
        var services = new ServiceCollection();
        services.AddGlobalResiliencePolicy(request =>
        {
            seen = request;
            return ResiliencePolicy.Create().Retry(request is Ping ? 3 : 1).Build();
        });

        using var provider = services.BuildServiceProvider();
        var request = new Ping(9);
        var policy = provider.GetRequiredService<IGlobalResiliencePolicyProvider>().GetPolicy(request);

        Assert.Same(request, seen);
        Assert.NotNull(policy);
    }

    [Fact]
    public void AddResilienceRegistry_RegistersOneSharedRegistry()
    {
        var services = new ServiceCollection();
        services.AddResilienceRegistry();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ICircuitBreakerRegistry>();

        Assert.IsType<CircuitBreakerRegistry>(registry);
        Assert.Same(registry, provider.GetRequiredService<ICircuitBreakerRegistry>());
    }
}

public class ResiliencePresetsTests
{
    [Theory]
    [InlineData("external")]
    [InlineData("database")]
    [InlineData("critical")]
    public async Task EveryPreset_LetsASuccessfulOperationThrough(string preset)
    {
        var policy = Build(preset, null);

        Assert.Equal(42, await policy.ExecuteAsync(() => Task.FromResult(42)));
    }

    [Theory]
    [InlineData("external")]
    [InlineData("database")]
    [InlineData("critical")]
    public async Task EveryPreset_AcceptsACustomOperationKey(string preset)
    {
        var policy = Build(preset, "custom-" + preset + "-" + Guid.NewGuid().ToString("N"));

        Assert.Equal("ok", await policy.ExecuteAsync(() => Task.FromResult("ok")));
    }

    [Fact]
    public async Task ForExternalApi_RetriesATransientFailureAndThenSucceeds()
    {
        var policy = ResiliencePolicy.Presets.ForExternalApi("external-retry-" + Guid.NewGuid().ToString("N"));
        var attempts = 0;

        var result = await policy.ExecuteAsync(() =>
        {
            attempts++;
            return attempts < 2
                ? throw new InvalidOperationException("transient")
                : Task.FromResult(attempts);
        });

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task ForDatabase_RetriesATransientFailureAndThenSucceeds()
    {
        var policy = ResiliencePolicy.Presets.ForDatabase("database-retry-" + Guid.NewGuid().ToString("N"));
        var attempts = 0;

        var result = await policy.ExecuteAsync(() =>
        {
            attempts++;
            return attempts < 2
                ? throw new InvalidOperationException("transient")
                : Task.FromResult(attempts);
        });

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task NoResilience_DoesNotRetryAndPropagatesTheOriginalException()
    {
        var policy = ResiliencePolicy.Presets.NoResilience();
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => policy.ExecuteAsync(() =>
        {
            attempts++;
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162
            return Task.FromResult(0);
#pragma warning restore CS0162
        }));

        Assert.Equal(1, attempts);
    }

    private static ResiliencePolicy Build(string preset, string? key) => preset switch
    {
        "external" => key is null ? ResiliencePolicy.Presets.ForExternalApi() : ResiliencePolicy.Presets.ForExternalApi(key),
        "database" => key is null ? ResiliencePolicy.Presets.ForDatabase() : ResiliencePolicy.Presets.ForDatabase(key),
        _ => key is null ? ResiliencePolicy.Presets.ForCritical() : ResiliencePolicy.Presets.ForCritical(key),
    };
}
