using Vali_Mediator_Resilience.Core.Enums;
using Xunit;
using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Policies;
using Vali_Mediator_Resilience.Core.Registry;

namespace Vali_Mediator_Resilience.Tests;

public class CircuitBreakerTests
{
    private static ResiliencePolicy BuildPolicy(
        string key,
        int failureThreshold = 3,
        TimeSpan? samplingDuration = null,
        TimeSpan? breakDuration = null,
        ICircuitBreakerRegistry? registry = null)
    {
        var builder = ResiliencePolicy.Create(key)
            .CircuitBreaker(o =>
            {
                o.CircuitKey = key;
                o.FailureThreshold = failureThreshold;
                o.SamplingDuration = samplingDuration ?? TimeSpan.FromSeconds(60);
                o.BreakDuration = breakDuration ?? TimeSpan.FromMilliseconds(100);
            });

        if (registry != null) builder.UseRegistry(registry);
        return builder.Build();
    }

    // -----------------------------------------------------------------------
    // State transitions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CircuitBreaker_StartsAsClosed_AllowsRequests()
    {
        var registry = new CircuitBreakerRegistry();
        var policy = BuildPolicy("cb-starts-closed", registry: registry);

        var result = await policy.ExecuteAsync(_ => Task.FromResult(42));

        Assert.Equal(42, result);
        Assert.Equal(CircuitState.Closed, registry.GetState("cb-starts-closed"));
    }

    [Fact]
    public async Task CircuitBreaker_ReachesThreshold_OpensCircuit()
    {
        var registry = new CircuitBreakerRegistry();
        var policy = BuildPolicy("cb-opens", failureThreshold: 3, registry: registry);

        // Cause 3 failures
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await policy.ExecuteAsync<int>(_ => throw new Exception("fail"));
            }
            catch { }
        }

        Assert.Equal(CircuitState.Open, registry.GetState("cb-opens"));
    }

    [Fact]
    public async Task CircuitBreaker_IsOpen_ThrowsCircuitOpenException()
    {
        var registry = new CircuitBreakerRegistry();
        var policy = BuildPolicy("cb-rejects", failureThreshold: 2, registry: registry);

        // Open the circuit
        for (int i = 0; i < 2; i++)
        {
            try { await policy.ExecuteAsync<int>(_ => throw new Exception("fail")); } catch { }
        }

        Assert.Equal(CircuitState.Open, registry.GetState("cb-rejects"));

        await Assert.ThrowsAsync<CircuitOpenException>(() =>
            policy.ExecuteAsync(_ => Task.FromResult(1)));
    }

    [Fact]
    public async Task CircuitBreaker_AfterBreakDuration_TransitionsToHalfOpen()
    {
        var registry = new CircuitBreakerRegistry();
        var policy = BuildPolicy("cb-halfopen",
            failureThreshold: 2,
            breakDuration: TimeSpan.FromMilliseconds(50),
            registry: registry);

        // Open the circuit
        for (int i = 0; i < 2; i++)
        {
            try { await policy.ExecuteAsync<int>(_ => throw new Exception("fail")); } catch { }
        }

        Assert.Equal(CircuitState.Open, registry.GetState("cb-halfopen"));

        await Task.Delay(80); // wait for break duration

        // Next call should be allowed (HalfOpen probe)
        int result = await policy.ExecuteAsync(_ => Task.FromResult(99));

        Assert.Equal(99, result);
        Assert.Equal(CircuitState.Closed, registry.GetState("cb-halfopen"));
    }

    [Fact]
    public async Task CircuitBreaker_HalfOpenProbeSucceeds_ClosesCircuit()
    {
        var registry = new CircuitBreakerRegistry();
        var policy = BuildPolicy("cb-probe-success",
            failureThreshold: 2,
            breakDuration: TimeSpan.FromMilliseconds(50),
            registry: registry);

        for (int i = 0; i < 2; i++)
        {
            try { await policy.ExecuteAsync<int>(_ => throw new Exception("fail")); } catch { }
        }

        await Task.Delay(80);

        await policy.ExecuteAsync(_ => Task.FromResult(1));

        Assert.Equal(CircuitState.Closed, registry.GetState("cb-probe-success"));
    }

    [Fact]
    public async Task CircuitBreaker_HalfOpenProbeFails_ReopensCircuit()
    {
        var registry = new CircuitBreakerRegistry();
        var policy = BuildPolicy("cb-probe-fail",
            failureThreshold: 2,
            breakDuration: TimeSpan.FromMilliseconds(50),
            registry: registry);

        // Open
        for (int i = 0; i < 2; i++)
        {
            try { await policy.ExecuteAsync<int>(_ => throw new Exception("fail")); } catch { }
        }

        await Task.Delay(80);

        // Probe fails — should reopen
        try { await policy.ExecuteAsync<int>(_ => throw new Exception("fail again")); } catch { }

        Assert.Equal(CircuitState.Open, registry.GetState("cb-probe-fail"));
    }

    // -----------------------------------------------------------------------
    // Callbacks
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CircuitBreaker_OnOpenCallback_InvokedWhenOpening()
    {
        bool onOpenCalled = false;
        var registry = new CircuitBreakerRegistry();

        var policy = ResiliencePolicy.Create("cb-callback")
            .CircuitBreaker(o =>
            {
                o.CircuitKey = "cb-callback";
                o.FailureThreshold = 2;
                o.SamplingDuration = TimeSpan.FromSeconds(60);
                o.BreakDuration = TimeSpan.FromSeconds(60);
                o.OnOpen = (_, _) => { onOpenCalled = true; return Task.CompletedTask; };
            })
            .UseRegistry(registry)
            .Build();

        for (int i = 0; i < 2; i++)
        {
            try { await policy.ExecuteAsync<int>(_ => throw new Exception("fail")); } catch { }
        }

        Assert.True(onOpenCalled);
    }

    // -----------------------------------------------------------------------
    // Shared registry
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CircuitBreaker_SharedRegistry_TwoPoliciesSameKey_ShareState()
    {
        var registry = new CircuitBreakerRegistry();

        var policy1 = ResiliencePolicy.Create("shared-key")
            .CircuitBreaker(o =>
            {
                o.CircuitKey = "shared-key";
                o.FailureThreshold = 2;
                o.SamplingDuration = TimeSpan.FromSeconds(60);
                o.BreakDuration = TimeSpan.FromSeconds(60);
            })
            .UseRegistry(registry)
            .Build();

        var policy2 = ResiliencePolicy.Create("shared-key")
            .CircuitBreaker(o =>
            {
                o.CircuitKey = "shared-key";
                o.FailureThreshold = 2;
                o.SamplingDuration = TimeSpan.FromSeconds(60);
                o.BreakDuration = TimeSpan.FromSeconds(60);
            })
            .UseRegistry(registry)
            .Build();

        // Open via policy1
        for (int i = 0; i < 2; i++)
        {
            try { await policy1.ExecuteAsync<int>(_ => throw new Exception("fail")); } catch { }
        }

        // policy2 should also see the open circuit
        await Assert.ThrowsAsync<CircuitOpenException>(() =>
            policy2.ExecuteAsync(_ => Task.FromResult(1)));
    }

    // -----------------------------------------------------------------------
    // Registry management
    // -----------------------------------------------------------------------

    [Fact]
    public void CircuitBreakerRegistry_Reset_ClosesCircuit()
    {
        var registry = new CircuitBreakerRegistry();
        var options = new CircuitBreakerOptions
        {
            CircuitKey = "reset-test",
            FailureThreshold = 2,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromSeconds(60)
        };

        var state = registry.GetOrCreate("reset-test", options);

        state.RecordFailure();
        state.RecordFailure();

        Assert.Equal(CircuitState.Open, state.State);

        registry.Reset("reset-test");

        Assert.Equal(CircuitState.Closed, state.State);
    }

    [Fact]
    public void CircuitBreakerRegistry_GetState_ReturnsNullForUnknownKey()
    {
        var registry = new CircuitBreakerRegistry();
        Assert.Null(registry.GetState("does-not-exist"));
    }

    // -----------------------------------------------------------------------
    // Rate-based threshold
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CircuitBreaker_RateBased_OpensWhenRateExceeded()
    {
        var registry = new CircuitBreakerRegistry();

        var policy = ResiliencePolicy.Create("rate-cb")
            .CircuitBreaker(o =>
            {
                o.CircuitKey = "rate-cb";
                o.FailureRateThreshold = 0.5; // open at 50% failure rate
                o.MinimumThroughput = 4;
                o.SamplingDuration = TimeSpan.FromSeconds(60);
                o.BreakDuration = TimeSpan.FromSeconds(60);
            })
            .UseRegistry(registry)
            .Build();

        // 2 successes, 3 failures = 60% failure rate (exceeds 50%)
        for (int i = 0; i < 2; i++)
        {
            try { await policy.ExecuteAsync(_ => Task.FromResult(1)); } catch { }
        }
        for (int i = 0; i < 3; i++)
        {
            try { await policy.ExecuteAsync<int>(_ => throw new Exception("fail")); } catch { }
        }

        Assert.Equal(CircuitState.Open, registry.GetState("rate-cb"));
    }
}
