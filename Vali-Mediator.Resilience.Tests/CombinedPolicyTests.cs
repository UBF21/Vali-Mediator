using Vali_Mediator.Core.Result;
using Xunit;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Policies;
using Vali_Mediator_Resilience.Core.Registry;

namespace Vali_Mediator_Resilience.Tests;

public class CombinedPolicyTests
{
    // -----------------------------------------------------------------------
    // Retry + Circuit Breaker
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RetryAndCircuitBreaker_OpenCircuitStopsRetries()
    {
        // Circuit opens after 2 failures. Retry would try 5 times but CB blocks after 2.
        int calls = 0;
        var registry = new CircuitBreakerRegistry();

        var policy = ResiliencePolicy.Create("combined-cb-retry")
            .Retry(options =>
            {
                options.MaxRetries = 5;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
            })
            .CircuitBreaker(options =>
            {
                options.CircuitKey = "combined-cb-retry";
                options.FailureThreshold = 2;
                options.SamplingDuration = TimeSpan.FromSeconds(60);
                options.BreakDuration = TimeSpan.FromSeconds(60);
            })
            .UseRegistry(registry)
            .Build();

        await Assert.ThrowsAsync<CircuitOpenException>(() =>
            policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new Exception("fail");
            }));

        // After CB opens, retries stop — calls = 2 (threshold) + 1 (first rejected attempt)
        Assert.True(calls <= 3);
        Assert.Equal(CircuitState.Open, registry.GetState("combined-cb-retry"));
    }

    // -----------------------------------------------------------------------
    // Retry + Timeout
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RetryAndTimeout_RetriesOnEachTimeout()
    {
        int calls = 0;

        var policy = ResiliencePolicy.Create()
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
            })
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromMilliseconds(50);
                options.Strategy = TimeoutStrategy.Pessimistic;
            })
            .Build();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            policy.ExecuteAsync(async _ =>
            {
                calls++;
                await Task.Delay(TimeSpan.FromSeconds(5));
                return 0;
            }));

        Assert.Equal(3, calls); // 1 initial + 2 retries each timing out
    }

    // -----------------------------------------------------------------------
    // Retry + Fallback
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RetryAndFallback_ExhaustsRetriesThenFallsBack()
    {
        int calls = 0;

        var policy = ResiliencePolicy.Create()
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
            })
            .Fallback<string>(options =>
            {
                options.FallbackValue = "fallback-value";
            });

        var result = await policy.ExecuteAsync(_ =>
        {
            calls++;
            throw new Exception("always fails");
        });

        Assert.Equal("fallback-value", result);
        Assert.Equal(3, calls); // 1 + 2 retries before fallback
    }

    // -----------------------------------------------------------------------
    // Full composite (Retry + CB + Timeout + Fallback)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FullComposite_ReturnsResultOnSuccess()
    {
        var registry = new CircuitBreakerRegistry();

        var policy = ResiliencePolicy.Create("full-composite")
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
            })
            .CircuitBreaker(options =>
            {
                options.CircuitKey = "full-composite";
                options.FailureThreshold = 10;
                options.SamplingDuration = TimeSpan.FromSeconds(60);
                options.BreakDuration = TimeSpan.FromSeconds(60);
            })
            .Timeout(TimeSpan.FromSeconds(5))
            .UseRegistry(registry)
            .Fallback<int>(options =>
            {
                options.FallbackValue = -999;
            });

        var result = await policy.ExecuteAsync(_ => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task FullComposite_AllFail_ReturnsFallback()
    {
        var registry = new CircuitBreakerRegistry();

        var policy = ResiliencePolicy.Create("full-composite-fail")
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
            })
            .CircuitBreaker(options =>
            {
                options.CircuitKey = "full-composite-fail";
                options.FailureThreshold = 100; // won't open
                options.SamplingDuration = TimeSpan.FromSeconds(60);
                options.BreakDuration = TimeSpan.FromSeconds(60);
            })
            .Timeout(TimeSpan.FromSeconds(5))
            .UseRegistry(registry)
            .Fallback<int>(options =>
            {
                options.FallbackValue = -1;
            });

        var result = await policy.ExecuteAsync(_ => throw new Exception("always fails"));

        Assert.Equal(-1, result);
    }

    // -----------------------------------------------------------------------
    // Presets
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Preset_ForExternalApi_SuccessfulCall()
    {
        var policy = ResiliencePolicy.Presets.ForExternalApi("test-external-api");
        var result = await policy.ExecuteAsync(_ => Task.FromResult("data"));
        Assert.Equal("data", result);
    }

    [Fact]
    public async Task Preset_ForDatabase_SuccessfulCall()
    {
        var policy = ResiliencePolicy.Presets.ForDatabase("test-db");
        var result = await policy.ExecuteAsync(_ => Task.FromResult(42));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Preset_NoResilience_PassesThrough()
    {
        var policy = ResiliencePolicy.Presets.NoResilience();
        var result = await policy.ExecuteAsync(_ => Task.FromResult("ok"));
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task Preset_NoResilience_PropagatesException()
    {
        var policy = ResiliencePolicy.Presets.NoResilience();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ => throw new InvalidOperationException("no retry")));
    }

    // -----------------------------------------------------------------------
    // ResilienceContext propagation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Context_AttemptNumber_IncreasesOnEachRetry()
    {
        var attempts = new List<int>();

        var policy = ResiliencePolicy.Create("ctx-test")
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
                options.OnRetry = (ctx, _) =>
                {
                    attempts.Add(ctx.AttemptNumber);
                    return Task.CompletedTask;
                };
            })
            .Build();

        await Assert.ThrowsAsync<Exception>(() =>
            policy.ExecuteAsync<int>(_ => throw new Exception("fail")));

        Assert.Equal(3, attempts.Count);
        Assert.Equal(new List<int> { 0, 1, 2 }, attempts);
    }

    [Fact]
    public async Task Context_OperationKey_SetFromCreate()
    {
        string? capturedKey = null;

        var policy = ResiliencePolicy.Create("my-operation")
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
                options.OnRetry = (ctx, _) =>
                {
                    capturedKey = ctx.OperationKey;
                    return Task.CompletedTask;
                };
            })
            .Build();

        await Assert.ThrowsAsync<Exception>(() =>
            policy.ExecuteAsync<int>(_ => throw new Exception("fail")));

        Assert.Equal("my-operation", capturedKey);
    }

    // -----------------------------------------------------------------------
    // IResult interface integration
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IResult_SuccessResult_NotTreatedAsFailure()
    {
        var policy = ResiliencePolicy.Create()
            .CircuitBreaker(o =>
            {
                o.CircuitKey = "iresult-success";
                o.FailureThreshold = 2;
                o.SamplingDuration = TimeSpan.FromSeconds(60);
                o.BreakDuration = TimeSpan.FromSeconds(60);
            })
            .Build();

        // Success results should not count as circuit breaker failures
        for (int i = 0; i < 5; i++)
        {
            var r = await policy.ExecuteAsync(_ =>
                Task.FromResult(Result<int>.Ok(i)));
            Assert.True(r.IsSuccess);
        }
    }

    [Fact]
    public async Task IResult_FailureResult_CountedAsCircuitFailure()
    {
        var registry = new CircuitBreakerRegistry();

        var policy = ResiliencePolicy.Create("iresult-fail")
            .CircuitBreaker(o =>
            {
                o.CircuitKey = "iresult-fail";
                o.FailureThreshold = 3;
                o.SamplingDuration = TimeSpan.FromSeconds(60);
                o.BreakDuration = TimeSpan.FromSeconds(60);
            })
            .UseRegistry(registry)
            .Build();

        for (int i = 0; i < 3; i++)
        {
            await policy.ExecuteAsync(_ =>
                Task.FromResult(Result<int>.Fail("error", ErrorType.Failure)));
        }

        Assert.Equal(CircuitState.Open, registry.GetState("iresult-fail"));
    }
}
