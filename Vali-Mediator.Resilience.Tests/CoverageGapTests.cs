using Vali_Mediator.Core.Result;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Pipeline;
using Vali_Mediator_Resilience.Core.Policies;
using Vali_Mediator_Resilience.Core.Registry;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class RetryResultPredicateTests
{
    private static string Key() => "gap-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task RetryOnResultPredicate_RetriesAPlainValueUntilItIsAccepted()
    {
        var policy = ResiliencePolicy.Create(Key())
            .Retry(o =>
            {
                o.MaxRetries = 4;
                o.BackoffType = BackoffType.Fixed;
                o.InitialDelay = TimeSpan.FromMilliseconds(1);
                o.RetryOnResultPredicate = r => r is int value && value < 3;
            })
            .Build();
        var attempts = 0;

        var result = await policy.ExecuteAsync(() => Task.FromResult(++attempts));

        Assert.Equal(3, result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task RetryOnResultPredicate_ReturnsTheLastValueWhenRetriesRunOut()
    {
        var policy = ResiliencePolicy.Create(Key())
            .Retry(o =>
            {
                o.MaxRetries = 2;
                o.BackoffType = BackoffType.Fixed;
                o.InitialDelay = TimeSpan.FromMilliseconds(1);
                o.RetryOnResultPredicate = _ => true;
            })
            .Build();
        var attempts = 0;

        var result = await policy.ExecuteAsync(() => Task.FromResult(++attempts));

        Assert.Equal(3, attempts);
        Assert.Equal(3, result);
    }

    [Fact]
    public void CalculateDelay_CustomBackoffWithoutAFactoryIsRejected()
    {
        var options = new RetryOptions { BackoffType = BackoffType.Custom };

        var ex = Assert.Throws<InvalidOperationException>(() => RetryMiddleware.CalculateDelay(options, 1));

        Assert.Contains("CustomDelayFactory", ex.Message);
    }

    [Fact]
    public void CalculateDelay_AnUnknownBackoffFallsBackToTheInitialDelay()
    {
        var options = new RetryOptions
        {
            BackoffType = (BackoffType)999,
            InitialDelay = TimeSpan.FromMilliseconds(7),
            MaxDelay = TimeSpan.FromSeconds(1)
        };

        Assert.Equal(TimeSpan.FromMilliseconds(7), RetryMiddleware.CalculateDelay(options, 3));
    }
}

public class BuilderAndPolicySurfaceTests
{
    [Fact]
    public void CircuitBreaker_WithoutAnyKeyIsRejected()
    {
        var builder = ResiliencePolicy.Create();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.CircuitBreaker(o => o.CircuitKey = "  "));

        Assert.Contains("CircuitKey", ex.Message);
    }

    [Fact]
    public void CircuitBreaker_InheritsTheOperationKeyWhenNoneIsGiven()
    {
        var builder = ResiliencePolicy.Create("orders").CircuitBreaker(o => { });

        Assert.Equal("orders", builder.CircuitBreakerOptions!.CircuitKey);
    }

    [Fact]
    public void EveryConfiguredPolicyIsExposedToTheBuilder()
    {
        var builder = ResiliencePolicy.Create("all")
            .Retry(2)
            .CircuitBreaker(o => o.CircuitKey = "gap-" + Guid.NewGuid().ToString("N"))
            .Timeout(TimeSpan.FromSeconds(5))
            .Bulkhead(2, 1)
            .Hedge(TimeSpan.FromSeconds(1))
            .RateLimiter(5)
            .Chaos(0);

        Assert.NotNull(builder.RetryOptions);
        Assert.NotNull(builder.CircuitBreakerOptions);
        Assert.NotNull(builder.TimeoutOptions);
        Assert.NotNull(builder.BulkheadOptions);
        Assert.NotNull(builder.HedgeOptions);
        Assert.NotNull(builder.RateLimiterOptions);
        Assert.NotNull(builder.ChaosOptions);
    }

    [Fact]
    public void AnUnconfiguredBuilderExposesNoOptions()
    {
        var builder = ResiliencePolicy.Create();

        Assert.Null(builder.RetryOptions);
        Assert.Null(builder.CircuitBreakerOptions);
        Assert.Null(builder.TimeoutOptions);
        Assert.Null(builder.BulkheadOptions);
        Assert.Null(builder.HedgeOptions);
        Assert.Null(builder.RateLimiterOptions);
        Assert.Null(builder.ChaosOptions);
    }

    [Fact]
    public async Task ParameterlessVoidOverload_RunsTheOperation()
    {
        var policy = ResiliencePolicy.Create().Build();
        var ran = false;

        await policy.ExecuteAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.True(ran);
    }

    [Fact]
    public async Task ParameterlessTypedOverload_ReturnsTheValue()
    {
        var policy = ResiliencePolicy.Create().Build();

        Assert.Equal("v", await policy.ExecuteAsync(() => Task.FromResult("v")));
    }

    [Fact]
    public async Task TypedFallbackPolicy_ParameterlessOverloadUsesTheFallback()
    {
        var policy = ResiliencePolicy.Create().Fallback<int>(o => o.FallbackValue = -1);

        var result = await policy.ExecuteAsync(() => Task.FromException<int>(new InvalidOperationException("x")));

        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task TypedFallbackPolicy_ReturnsTheOperationValueWhenItSucceeds()
    {
        var policy = ResiliencePolicy.Create().Fallback<int>(o => o.FallbackValue = -1);

        Assert.Equal(5, await policy.ExecuteAsync(_ => Task.FromResult(5)));
    }
}

public class PolicyMiddlewareGapTests
{
    private static string Key() => "gap-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Chaos_WhenInjectingWithoutAnyFaultConfiguredStillRunsTheOperation()
    {
        var injected = 0;
        var policy = ResiliencePolicy.Create()
            .Chaos(o =>
            {
                o.InjectionRate = 1;
                o.OnChaosInjected = () =>
                {
                    injected++;
                    return Task.CompletedTask;
                };
            })
            .Build();

        var result = await policy.ExecuteAsync(() => Task.FromResult("real"));

        Assert.Equal("real", result);
        Assert.Equal(1, injected);
    }

    [Fact]
    public async Task CircuitBreaker_AFailedResultOpensTheCircuitAndReportsNoException()
    {
        var opened = new List<Exception?>();
        var policy = ResiliencePolicy.Create()
            .CircuitBreaker(o =>
            {
                o.CircuitKey = Key();
                o.FailureThreshold = 2;
                o.OnOpen = (_, ex) =>
                {
                    opened.Add(ex);
                    return Task.CompletedTask;
                };
            })
            .Build();

        await policy.ExecuteAsync(() => Task.FromResult(Result<int>.Fail("first")));
        await policy.ExecuteAsync(() => Task.FromResult(Result<int>.Fail("second")));

        var single = Assert.Single(opened);
        Assert.Null(single);
        await Assert.ThrowsAsync<Vali_Mediator_Resilience.Core.Exceptions.CircuitOpenException>(
            () => policy.ExecuteAsync(() => Task.FromResult(Result<int>.Ok(1))));
    }

    [Fact]
    public async Task Hedge_ReturnsTheLastResultWhenEveryAttemptIsRejected()
    {
        var attempts = 0;
        var policy = ResiliencePolicy.Create()
            .Hedge(o =>
            {
                o.HedgeDelay = TimeSpan.FromMilliseconds(1);
                o.MaxHedgedAttempts = 1;
                o.ShouldHedgeOnResult = _ => true;
            })
            .Build();

        var result = await policy.ExecuteAsync(async ct =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(TimeSpan.FromMilliseconds(30), CancellationToken.None);
            return "value";
        });

        Assert.Equal("value", result);
        Assert.Equal(2, Volatile.Read(ref attempts));
    }

    [Fact]
    public async Task Timeout_InvokesOnTimeoutBeforeThrowing()
    {
        var notified = 0;
        var policy = ResiliencePolicy.Create()
            .Timeout(o =>
            {
                o.Timeout = TimeSpan.FromMilliseconds(50);
                o.OnTimeout = _ =>
                {
                    notified++;
                    return Task.CompletedTask;
                };
            })
            .Build();

        await Assert.ThrowsAsync<TimeoutException>(() => policy.ExecuteAsync(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 1;
        }));

        Assert.Equal(1, notified);
    }
}

public class RegistryAndStateGapTests
{
    private static CircuitBreakerOptions Options() => new CircuitBreakerOptions
    {
        CircuitKey = "gap",
        FailureThreshold = 3,
        SamplingDuration = TimeSpan.FromSeconds(10),
        BreakDuration = TimeSpan.FromSeconds(30)
    };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Registry_RejectsABlankCircuitKey(string key)
    {
        var registry = new CircuitBreakerRegistry();

        Assert.Throws<ArgumentException>(() => registry.GetOrCreate(key, Options()));
    }

    [Fact]
    public void Registry_ClearForgetsEveryCircuit()
    {
        var registry = new CircuitBreakerRegistry();
        registry.GetOrCreate("a", Options());
        registry.GetOrCreate("b", Options());

        registry.Clear();

        Assert.Null(registry.GetState("a"));
        Assert.Null(registry.GetState("b"));
    }

    [Fact]
    public void State_PublicCanExecuteAndRecordSuccessWorkOnAClosedCircuit()
    {
        var state = new CircuitBreakerState(Options());

        Assert.True(state.CanExecute());
        state.RecordSuccess();

        Assert.Equal(CircuitState.Closed, state.State);
    }

    [Fact]
    public void State_PublicRecordFailureCountsTowardTheThreshold()
    {
        var state = new CircuitBreakerState(Options());

        state.RecordFailure();
        state.RecordFailure();
        Assert.Equal(CircuitState.Closed, state.State);
        state.RecordFailure();

        Assert.Equal(CircuitState.Open, state.State);
        Assert.False(state.CanExecute());
    }

    [Fact]
    public void State_FailuresOlderThanTheSamplingWindowAreForgotten()
    {
        var clock = new ManualClock();
        var state = new CircuitBreakerState(Options(), clock.Read);

        state.RecordFailure();
        state.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(11));
        state.RecordFailure();

        Assert.Equal(CircuitState.Closed, state.State);
    }
}

public class PartitionedRateLimiterDisposeTests
{
    [Fact]
    public void Dispose_ReleasesEveryPartitionIncludingTheOverflowLimiter()
    {
        var clock = new ManualClock();
        var options = new RateLimiterOptions { MaxPartitions = 1 };
        var partitioned = new PartitionedRateLimiterState(options, clock.Read);
        var first = partitioned.GetOrCreate("a");
        var overflow = partitioned.GetOrCreate("b");

        Assert.NotSame(first, overflow);
        partitioned.Dispose();
        partitioned.Dispose();
    }

    [Fact]
    public void Dispose_WorksWhenTheOverflowLimiterWasNeverNeeded()
    {
        var clock = new ManualClock();
        var partitioned = new PartitionedRateLimiterState(new RateLimiterOptions { MaxPartitions = 5 }, clock.Read);
        partitioned.GetOrCreate("a");

        partitioned.Dispose();
    }
}
