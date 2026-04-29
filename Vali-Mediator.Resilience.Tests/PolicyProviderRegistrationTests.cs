using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Xunit;
using Vali_Mediator_Resilience.Core.Policies;
using Vali_Mediator_Resilience.Integration;

namespace Vali_Mediator_Resilience.Tests;

// ── Stub types used only in these tests ────────────────────────────────────

public class SampleRequest;
public class AnotherRequest;
public class UnrelatedRequest;

public class SampleRequestPolicyProvider : IResiliencePolicyProvider<SampleRequest>
{
    public ResiliencePolicy GetPolicy(SampleRequest request) =>
        ResiliencePolicy.Create().Retry(1).Build();
}

public class AnotherRequestPolicyProvider : IResiliencePolicyProvider<AnotherRequest>
{
    public ResiliencePolicy GetPolicy(AnotherRequest request) =>
        ResiliencePolicy.Create().Timeout(TimeSpan.FromSeconds(5)).Build();
}

// ── Tests ───────────────────────────────────────────────────────────────────

public class PolicyProviderRegistrationTests
{
    // -----------------------------------------------------------------------
    // RegisterResiliencePoliciesFromAssembly
    // -----------------------------------------------------------------------

    [Fact]
    public void RegisterResiliencePoliciesFromAssembly_RegistersAllProviders()
    {
        var services = new ServiceCollection();
        services.RegisterResiliencePoliciesFromAssembly(typeof(PolicyProviderRegistrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        var sampleProvider = provider.GetService<IResiliencePolicyProvider<SampleRequest>>();
        var anotherProvider = provider.GetService<IResiliencePolicyProvider<AnotherRequest>>();

        Assert.NotNull(sampleProvider);
        Assert.NotNull(anotherProvider);
        Assert.IsType<SampleRequestPolicyProvider>(sampleProvider);
        Assert.IsType<AnotherRequestPolicyProvider>(anotherProvider);
    }

    [Fact]
    public void RegisterResiliencePoliciesFromAssembly_DoesNotRegisterUnrelatedTypes()
    {
        var services = new ServiceCollection();
        services.RegisterResiliencePoliciesFromAssembly(typeof(PolicyProviderRegistrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        var unrelated = provider.GetService<IResiliencePolicyProvider<UnrelatedRequest>>();
        Assert.Null(unrelated);
    }

    [Fact]
    public void RegisterResiliencePoliciesFromAssembly_DefaultLifetimeIsScoped()
    {
        var services = new ServiceCollection();
        services.RegisterResiliencePoliciesFromAssembly(typeof(PolicyProviderRegistrationTests).Assembly);

        var descriptor = services.First(d =>
            d.ServiceType == typeof(IResiliencePolicyProvider<SampleRequest>));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void RegisterResiliencePoliciesFromAssembly_RespectsLifetimeOverride()
    {
        var services = new ServiceCollection();
        services.RegisterResiliencePoliciesFromAssembly(
            typeof(PolicyProviderRegistrationTests).Assembly,
            ServiceLifetime.Singleton);

        var descriptor = services.First(d =>
            d.ServiceType == typeof(IResiliencePolicyProvider<SampleRequest>));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void RegisterResiliencePoliciesFromAssembly_ThrowsWhenServicesIsNull()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() =>
            services.RegisterResiliencePoliciesFromAssembly(typeof(PolicyProviderRegistrationTests).Assembly));
    }

    [Fact]
    public void RegisterResiliencePoliciesFromAssembly_ThrowsWhenAssemblyIsNull()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.RegisterResiliencePoliciesFromAssembly(null!));
    }

    // -----------------------------------------------------------------------
    // RegisterResiliencePoliciesFromAssemblyContaining<T>
    // -----------------------------------------------------------------------

    [Fact]
    public void RegisterResiliencePoliciesFromAssemblyContaining_RegistersAllProviders()
    {
        var services = new ServiceCollection();
        services.RegisterResiliencePoliciesFromAssemblyContaining<PolicyProviderRegistrationTests>();

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IResiliencePolicyProvider<SampleRequest>>());
        Assert.NotNull(provider.GetService<IResiliencePolicyProvider<AnotherRequest>>());
    }

    [Fact]
    public void RegisterResiliencePoliciesFromAssemblyContaining_DefaultLifetimeIsScoped()
    {
        var services = new ServiceCollection();
        services.RegisterResiliencePoliciesFromAssemblyContaining<PolicyProviderRegistrationTests>();

        var descriptor = services.First(d =>
            d.ServiceType == typeof(IResiliencePolicyProvider<SampleRequest>));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void RegisterResiliencePoliciesFromAssemblyContaining_RespectsLifetimeOverride()
    {
        var services = new ServiceCollection();
        services.RegisterResiliencePoliciesFromAssemblyContaining<PolicyProviderRegistrationTests>(
            ServiceLifetime.Transient);

        var descriptor = services.First(d =>
            d.ServiceType == typeof(IResiliencePolicyProvider<SampleRequest>));

        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    // -----------------------------------------------------------------------
    // Registered providers produce valid policies
    // -----------------------------------------------------------------------

    [Fact]
    public void RegisteredProvider_GetPolicy_ReturnsValidPolicy()
    {
        var services = new ServiceCollection();
        services.RegisterResiliencePoliciesFromAssembly(typeof(PolicyProviderRegistrationTests).Assembly);

        var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IResiliencePolicyProvider<SampleRequest>>();

        var policy = policyProvider.GetPolicy(new SampleRequest());

        Assert.NotNull(policy);
    }
}
