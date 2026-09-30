using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Pipeline;
using Vali_Mediator_Resilience.Core.Policies;
using Vali_Mediator_Resilience.Integration;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

/// <summary>Limits configured through the library's registration and options, across value matrices.</summary>
[Collection("Serial")]
public class ResilienceConfigurationTests
{
    private static string Key() => "cfg-" + Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void MaxSharedStates_RejectsNonPositive(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ResilienceGlobalOptions { MaxSharedStates = value });
        Assert.Equal("MaxSharedStates", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ServiceCollection().AddResilienceOptions(o => o.MaxSharedStates = value));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10_000)]
    [InlineData(int.MaxValue)]
    public void MaxSharedStates_AcceptsPositive(int value)
        => Assert.Equal(value, new ResilienceGlobalOptions { MaxSharedStates = value }.MaxSharedStates);

    [Fact]
    public void MaxSharedStates_DefaultIsUnchanged()
        => Assert.Equal(10_000, new ResilienceGlobalOptions().MaxSharedStates);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void AddResilienceOptions_BoundsTheNumberOfSharedStateKeys(int extraKeys)
    {
        int previous = SharedPolicyStates.Store.Limit;
        try
        {
            new ServiceCollection().AddResilienceOptions(o => o.MaxSharedStates = SharedPolicyStates.Store.Count + extraKeys);

            for (var i = 0; i < extraKeys; i++)
                ResiliencePolicy.Create().Bulkhead(1).WithSharedState(Key()).Build();

            Assert.Throws<InvalidOperationException>(
                () => ResiliencePolicy.Create().Bulkhead(1).WithSharedState(Key()).Build());
        }
        finally
        {
            SharedPolicyStates.Store.SetLimit(previous);
        }
    }

    [Fact]
    public void AddResilienceOptions_RegistersTheOptionsSingleton()
    {
        int previous = SharedPolicyStates.Store.Limit;
        try
        {
            var services = new ServiceCollection();
            services.AddResilienceOptions(o => o.MaxSharedStates = previous);
            using var provider = services.BuildServiceProvider();

            Assert.Equal(previous, provider.GetRequiredService<ResilienceGlobalOptions>().MaxSharedStates);
        }
        finally
        {
            SharedPolicyStates.Store.SetLimit(previous);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task Bulkhead_MaxConcurrentCalls_IsEnforcedAtEveryConfiguredSize(int max)
    {
        var policy = ResiliencePolicy.Create().Bulkhead(max, 0).Build();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Enumerable.Range(0, max)
            .Select(_ => policy.ExecuteAsync(async ct => { await gate.Task; return 1; }))
            .ToList();

        await Assert.ThrowsAsync<BulkheadRejectedException>(() => policy.ExecuteAsync(_ => Task.FromResult(2)));

        gate.SetResult(true);
        await Task.WhenAll(running);
        Assert.Equal(3, await policy.ExecuteAsync(_ => Task.FromResult(3)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task RateLimiter_BucketCapacity_AdmitsExactlyCapacityCalls(int capacity)
    {
        var policy = ResiliencePolicy.Create()
            .RateLimiter(o => { o.BucketCapacity = capacity; o.TokensPerInterval = 0; })
            .Build();

        for (var i = 0; i < capacity; i++)
            await policy.ExecuteAsync(_ => Task.FromResult(i));

        await Assert.ThrowsAsync<RateLimitExceededException>(() => policy.ExecuteAsync(_ => Task.FromResult(0)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task RateLimiter_SlidingWindow_PermitLimit_AdmitsExactlyThatManyCalls(int permits)
    {
        var policy = ResiliencePolicy.Create()
            .RateLimiter(o =>
            {
                o.Algorithm = Vali_Mediator_Resilience.Core.Enums.RateLimiterAlgorithm.SlidingWindow;
                o.PermitLimit = permits;
                o.Window = TimeSpan.FromMinutes(10);
            })
            .Build();

        for (var i = 0; i < permits; i++)
            await policy.ExecuteAsync(_ => Task.FromResult(i));

        await Assert.ThrowsAsync<RateLimitExceededException>(() => policy.ExecuteAsync(_ => Task.FromResult(0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RateLimiter_MaxPartitions_RejectsNonPositiveAtBuild(int maxPartitions)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ResiliencePolicy.Create().RateLimiter(o => o.MaxPartitions = maxPartitions).Build());

    [Theory]
    [InlineData(1)]
    [InlineData(10_000)]
    public void RateLimiter_MaxPartitions_AcceptsPositiveAtBuild(int maxPartitions)
        => ResiliencePolicy.Create().RateLimiter(o => o.MaxPartitions = maxPartitions).Build();
}
