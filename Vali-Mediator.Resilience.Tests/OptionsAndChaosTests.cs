using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Pipeline;
using Vali_Mediator_Resilience.Core.Policies;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class OptionsAndChaosTests
{
    // -----------------------------------------------------------------------
    // Builder validation (O-19)
    // -----------------------------------------------------------------------

    public static IEnumerable<object[]> InvalidConfigurations()
    {
        yield return Case("negative retries", b => b.Retry(o => o.MaxRetries = -1));
        yield return Case("negative initial delay", b => b.Retry(o => o.InitialDelay = TimeSpan.FromSeconds(-1)));
        yield return Case("NaN multiplier", b => b.Retry(o => o.Multiplier = double.NaN));
        yield return Case("zero failure threshold", b => b.CircuitBreaker(o => { o.CircuitKey = "k"; o.FailureThreshold = 0; }));
        yield return Case("failure rate above 1", b => b.CircuitBreaker(o => { o.CircuitKey = "k"; o.FailureRateThreshold = 1.5; }));
        yield return Case("zero break duration", b => b.CircuitBreaker(o => { o.CircuitKey = "k"; o.BreakDuration = TimeSpan.Zero; }));
        yield return Case("zero half-open attempts", b => b.CircuitBreaker(o => { o.CircuitKey = "k"; o.HalfOpenMaxAttempts = 0; }));
        yield return Case("zero timeout", b => b.Timeout(TimeSpan.Zero));
        yield return Case("zero concurrent calls", b => b.Bulkhead(0));
        yield return Case("negative queue", b => b.Bulkhead(1, -1));
        yield return Case("negative hedge delay", b => b.Hedge(TimeSpan.FromSeconds(-1)));
        yield return Case("chaos rate above 1", b => b.Chaos(2));
        yield return Case("zero replenishment interval", b => b.RateLimiter(o => o.ReplenishmentInterval = TimeSpan.Zero));
        yield return Case("zero max partitions", b => b.RateLimiter(o => o.MaxPartitions = 0));
    }

    private static object[] Case(string name, Func<ResiliencePolicyBuilder, ResiliencePolicyBuilder> configure)
        => new object[] { name, configure };

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void Build_RejectsOutOfRangeOptions(string name, Func<ResiliencePolicyBuilder, ResiliencePolicyBuilder> configure)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => configure(ResiliencePolicy.Create()).Build());
        _ = name;
    }

    [Fact]
    public void Build_RejectsOutOfRangeOptions_AlsoForTypedFallbackPolicies()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ResiliencePolicy.Create().Timeout(TimeSpan.Zero).Fallback<int>(o => o.FallbackValue = 1));
    }

    [Fact]
    public void Build_CustomBackoffWithoutFactory_FailsAtBuildTime()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ResiliencePolicy.Create().Retry(o => o.BackoffType = BackoffType.Custom).Build());
    }

    [Fact]
    public void Build_AcceptsRejectAllAndNoRefillSettings()
    {
        Assert.NotNull(ResiliencePolicy.Create().RateLimiter(o => { o.BucketCapacity = 0; o.TokensPerInterval = 0; }).Build());
        Assert.NotNull(ResiliencePolicy.Create()
            .RateLimiter(o => { o.Algorithm = RateLimiterAlgorithm.SlidingWindow; o.PermitLimit = 0; })
            .Build());
        Assert.NotNull(ResiliencePolicy.Create().Bulkhead(o => { o.MaxConcurrentCalls = 1; o.QueueTimeout = Timeout.InfiniteTimeSpan; }).Build());
    }

    // -----------------------------------------------------------------------
    // Backoff arithmetic (B-07)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(BackoffType.Exponential)]
    [InlineData(BackoffType.ExponentialWithJitter)]
    [InlineData(BackoffType.Linear)]
    public void CalculateDelay_HugeAttemptNumbers_ClampToMaxDelay(BackoffType backoff)
    {
        var options = new RetryOptions
        {
            BackoffType = backoff,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromSeconds(30),
            Multiplier = 10
        };

        var delay = RetryMiddleware.CalculateDelay(options, attempt: 100_000);

        Assert.InRange(delay, TimeSpan.Zero, options.MaxDelay);
    }

    // -----------------------------------------------------------------------
    // Chaos position (R-06)
    // -----------------------------------------------------------------------

    private static async Task<(int Retries, int OperationCalls)> RunWithChaos(bool perAttempt)
    {
        int retries = 0, calls = 0;
        var policy = ResiliencePolicy.Create()
            .Retry(o =>
            {
                o.MaxRetries = 2;
                o.BackoffType = BackoffType.Fixed;
                o.InitialDelay = TimeSpan.Zero;
                o.OnRetry = (_, _) => { retries++; return Task.CompletedTask; };
            })
            .Chaos(o =>
            {
                o.InjectionRate = 1;
                o.ExceptionFactory = () => new InvalidOperationException("chaos");
                o.InjectPerAttempt = perAttempt;
            })
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(_ => { calls++; return Task.FromResult(1); }));
        return (retries, calls);
    }

    [Fact]
    public async Task Chaos_ByDefault_IsOutsideRetry_SoRetryNeverSeesTheFault()
    {
        var (retries, calls) = await RunWithChaos(perAttempt: false);

        Assert.Equal(0, retries);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Chaos_InjectPerAttempt_IsInsideRetry_SoEveryAttemptFaults()
    {
        var (retries, calls) = await RunWithChaos(perAttempt: true);

        Assert.Equal(2, retries);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Chaos_InjectPerAttempt_FeedsTheCircuitBreaker()
    {
        var registry = new Vali_Mediator_Resilience.Core.Registry.CircuitBreakerRegistry();
        var policy = ResiliencePolicy.Create("chaos-cb")
            .CircuitBreaker(o => { o.CircuitKey = "chaos-cb"; o.FailureThreshold = 1; o.BreakDuration = TimeSpan.FromMinutes(5); })
            .Chaos(o =>
            {
                o.InjectionRate = 1;
                o.ExceptionFactory = () => new InvalidOperationException("chaos");
                o.InjectPerAttempt = true;
            })
            .UseRegistry(registry)
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => policy.ExecuteAsync(_ => Task.FromResult(1)));

        Assert.Equal(CircuitState.Open, registry.GetState("chaos-cb"));
    }
}
