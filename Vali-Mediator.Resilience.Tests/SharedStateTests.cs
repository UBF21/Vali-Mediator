using Vali_Mediator.Core.Request;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Pipeline;
using Vali_Mediator_Resilience.Core.Policies;
using Vali_Mediator_Resilience.Integration;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

/// <summary>A policy rebuilt on every request must still enforce its limits when marked with WithSharedState.</summary>
public class SharedStateTests
{
    // Static state: every test uses its own key.
    private static string Key() => "shared-" + Guid.NewGuid().ToString("N");

    private static TaskCompletionSource<bool> Gate() =>
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Bulkhead_LimitAppliesAcrossSeparatelyBuiltPolicies()
    {
        string key = Key();
        ResiliencePolicy Make() => ResiliencePolicy.Create().Bulkhead(1).WithSharedState(key).Build();
        var gate = Gate();
        var first = Make().ExecuteAsync(async _ => { await gate.Task; return 1; });

        await Assert.ThrowsAsync<BulkheadRejectedException>(() => Make().ExecuteAsync(_ => Task.FromResult(2)));

        gate.SetResult(true);
        await first;
    }

    [Fact]
    public async Task Bulkhead_WithoutSharedState_PoliciesAreIndependent()
    {
        ResiliencePolicy Make() => ResiliencePolicy.Create().Bulkhead(1).Build();
        var gate = Gate();
        var first = Make().ExecuteAsync(async _ => { await gate.Task; return 1; });

        Assert.Equal(2, await Make().ExecuteAsync(_ => Task.FromResult(2)));

        gate.SetResult(true);
        await first;
    }

    [Fact]
    public async Task RateLimiter_BucketIsSharedAcrossSeparatelyBuiltPolicies()
    {
        string key = Key();
        ResiliencePolicy Make() => ResiliencePolicy.Create()
            .RateLimiter(o => { o.BucketCapacity = 2; o.TokensPerInterval = 0; })
            .WithSharedState(key)
            .Build();

        await Make().ExecuteAsync(_ => Task.FromResult(1));
        await Make().ExecuteAsync(_ => Task.FromResult(1));

        await Assert.ThrowsAsync<RateLimitExceededException>(() => Make().ExecuteAsync(_ => Task.FromResult(1)));
    }

    [Fact]
    public async Task CircuitBreaker_StateIsSharedAcrossSeparatelyBuiltPolicies()
    {
        string key = Key();
        ResiliencePolicy Make() => ResiliencePolicy.Create()
            .CircuitBreaker(o => { o.CircuitKey = key; o.FailureThreshold = 1; o.BreakDuration = TimeSpan.FromMinutes(5); })
            .WithSharedState(key)
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Make().ExecuteAsync<int>(_ => throw new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<CircuitOpenException>(() => Make().ExecuteAsync(_ => Task.FromResult(1)));
    }

    [Fact]
    public async Task DifferentKeys_DoNotInterfere()
    {
        ResiliencePolicy Make(string key) => ResiliencePolicy.Create()
            .RateLimiter(o => { o.BucketCapacity = 1; o.TokensPerInterval = 0; })
            .WithSharedState(key)
            .Build();
        string a = Key(), b = Key();

        await Make(a).ExecuteAsync(_ => Task.FromResult(1));

        Assert.Equal(1, await Make(b).ExecuteAsync(_ => Task.FromResult(1)));
        await Assert.ThrowsAsync<RateLimitExceededException>(() => Make(a).ExecuteAsync(_ => Task.FromResult(1)));
    }

    [Fact]
    public async Task FirstPolicyToUseAKeyDefinesTheLimits()
    {
        string key = Key();
        ResiliencePolicy Make(int capacity) => ResiliencePolicy.Create()
            .RateLimiter(o => { o.BucketCapacity = capacity; o.TokensPerInterval = 0; })
            .WithSharedState(key)
            .Build();

        await Make(1).ExecuteAsync(_ => Task.FromResult(1));

        await Assert.ThrowsAsync<RateLimitExceededException>(() => Make(100).ExecuteAsync(_ => Task.FromResult(1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithSharedState_RejectsBlankKeys(string? key)
    {
        Assert.Throws<ArgumentException>(() => ResiliencePolicy.Create().WithSharedState(key!));
    }

    // -----------------------------------------------------------------------
    // The documented DI pattern: AddResiliencePolicy<T>(req => ...Build())
    // -----------------------------------------------------------------------

    public sealed record Login(string User) : IRequest<int>;

    [Fact]
    public async Task PolicyFactoryBuiltPerRequest_StillEnforcesTheLimit_WhenSharedStateIsSet()
    {
        string key = Key();
        var provider = new DelegateResiliencePolicyProvider<Login>(_ => ResiliencePolicy.Create()
            .RateLimiter(o => { o.BucketCapacity = 2; o.TokensPerInterval = 0; })
            .WithSharedState(key)
            .Build());
        var behavior = new ResilienceBehavior<Login, int>(new[] { provider }, Array.Empty<IGlobalResiliencePolicyProvider>());

        await behavior.Handle(new Login("a"), _ => Task.FromResult(1), CancellationToken.None);
        await behavior.Handle(new Login("b"), _ => Task.FromResult(1), CancellationToken.None);

        await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            behavior.Handle(new Login("c"), _ => Task.FromResult(1), CancellationToken.None));
    }

    [Fact]
    public async Task PolicyFactoryBuiltPerRequest_WithoutSharedState_NeverLimits()
    {
        var provider = new DelegateResiliencePolicyProvider<Login>(_ => ResiliencePolicy.Create()
            .RateLimiter(o => { o.BucketCapacity = 1; o.TokensPerInterval = 0; })
            .Build());
        var behavior = new ResilienceBehavior<Login, int>(new[] { provider }, Array.Empty<IGlobalResiliencePolicyProvider>());

        for (int i = 0; i < 5; i++)
            Assert.Equal(1, await behavior.Handle(new Login("a"), _ => Task.FromResult(1), CancellationToken.None));
    }

    [Fact]
    public void SharedStateStore_RefusesNewKeysBeyondItsCap()
    {
        var store = new PolicyStateStore(maxEntries: 3);
        for (int i = 0; i < 3; i++)
            store.GetOrAdd("k" + i, "test", () => new object());

        Assert.Same(store.GetOrAdd("k0", "test", () => new object()), store.GetOrAdd("k0", "test", () => new object()));
        Assert.Throws<InvalidOperationException>(() => store.GetOrAdd("k3", "test", () => new object()));
        Assert.Equal(3, store.Count);
    }
}
