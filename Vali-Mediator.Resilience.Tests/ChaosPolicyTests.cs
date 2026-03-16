using Vali_Mediator_Resilience.Core.Policies;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class ChaosPolicyTests
{
    // -----------------------------------------------------------------------
    // Injection disabled (rate = 0)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Chaos_InjectionRateZero_NeverInjectsFault()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Chaos(0.0, opts =>
            {
                opts.ExceptionFactory = () => new InvalidOperationException("chaos");
            })
            .Build();

        for (int i = 0; i < 20; i++)
        {
            var result = await policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                return Task.FromResult(1);
            });
            Assert.Equal(1, result);
        }

        Assert.Equal(20, calls);
    }

    // -----------------------------------------------------------------------
    // Always inject (rate = 1.0)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Chaos_InjectionRateOne_AlwaysThrowsException()
    {
        var policy = ResiliencePolicy.Create()
            .Chaos(1.0, opts =>
            {
                opts.ExceptionFactory = () => new InvalidOperationException("chaos exception");
            })
            .Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ => Task.FromResult(1)));

        Assert.Equal("chaos exception", ex.Message);
    }

    [Fact]
    public async Task Chaos_InjectionRateOne_InjectsLatencyThenExecutes()
    {
        bool operationExecuted = false;
        var policy = ResiliencePolicy.Create()
            .Chaos(1.0, opts =>
            {
                opts.LatencyInjection = TimeSpan.FromMilliseconds(50);
            })
            .Build();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await policy.ExecuteAsync<string>(_ =>
        {
            operationExecuted = true;
            return Task.FromResult("done");
        });
        sw.Stop();

        Assert.Equal("done", result);
        Assert.True(operationExecuted);
        Assert.True(sw.ElapsedMilliseconds >= 40, $"Expected >= 40ms but was {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Chaos_InjectionRateOne_ReturnsSyntheticResult()
    {
        bool operationExecuted = false;
        var policy = ResiliencePolicy.Create()
            .Chaos(1.0, opts =>
            {
                opts.ResultFactory = _ => "synthetic-value";
            })
            .Build();

        var result = await policy.ExecuteAsync<string>(_ =>
        {
            operationExecuted = true;
            return Task.FromResult("real-value");
        });

        Assert.Equal("synthetic-value", result);
        Assert.False(operationExecuted);
    }

    // -----------------------------------------------------------------------
    // Priority: exception > latency > result
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Chaos_ExceptionTakesPriorityOverLatency()
    {
        var policy = ResiliencePolicy.Create()
            .Chaos(opts =>
            {
                opts.InjectionRate = 1.0;
                opts.ExceptionFactory = () => new Exception("priority exception");
                opts.LatencyInjection = TimeSpan.FromSeconds(10); // would make test slow
            })
            .Build();

        // Should throw immediately, not delay 10 seconds
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<Exception>(() =>
            policy.ExecuteAsync<string>(_ => Task.FromResult("ok")));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1000, "Exception should take priority over latency");
    }

    [Fact]
    public async Task Chaos_LatencyTakesPriorityOverResult()
    {
        bool operationExecuted = false;
        var policy = ResiliencePolicy.Create()
            .Chaos(opts =>
            {
                opts.InjectionRate = 1.0;
                opts.LatencyInjection = TimeSpan.FromMilliseconds(30);
                opts.ResultFactory = _ => "synthetic";
            })
            .Build();

        // With latency + no exception, operation should execute (latency wins over result factory)
        var result = await policy.ExecuteAsync<string>(_ =>
        {
            operationExecuted = true;
            return Task.FromResult("real");
        });

        Assert.Equal("real", result);
        Assert.True(operationExecuted);
    }

    // -----------------------------------------------------------------------
    // OnChaosInjected callback
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Chaos_OnChaosInjected_CalledWhenFaultInjected()
    {
        bool callbackInvoked = false;
        var policy = ResiliencePolicy.Create()
            .Chaos(1.0, opts =>
            {
                opts.OnChaosInjected = () => { callbackInvoked = true; return Task.CompletedTask; };
                opts.ResultFactory = _ => "synthetic";
            })
            .Build();

        await policy.ExecuteAsync<string>(_ => Task.FromResult("real"));

        Assert.True(callbackInvoked);
    }

    [Fact]
    public async Task Chaos_OnChaosInjected_NotCalledWhenNoInjection()
    {
        bool callbackInvoked = false;
        // Deterministic RNG: always returns 0.99 which is >= InjectionRate of 0.5
        var rng = new DeterministicRandom(0.99);
        var policy = ResiliencePolicy.Create()
            .Chaos(opts =>
            {
                opts.InjectionRate = 0.5;
                opts.Random = rng;
                opts.OnChaosInjected = () => { callbackInvoked = true; return Task.CompletedTask; };
                opts.ExceptionFactory = () => new Exception("chaos");
            })
            .Build();

        // With NextDouble() = 0.99 and InjectionRate = 0.5, chaos should NOT fire
        await policy.ExecuteAsync<string>(_ => Task.FromResult("ok"));
        Assert.False(callbackInvoked);
    }

    // -----------------------------------------------------------------------
    // Custom Random (deterministic tests)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Chaos_CustomRandom_DeterminesWhetherFaultFires()
    {
        // rng always returns 0.0 → always below any InjectionRate > 0
        var alwaysFire = new DeterministicRandom(0.0);
        var policy = ResiliencePolicy.Create()
            .Chaos(opts =>
            {
                opts.InjectionRate = 0.5;
                opts.Random = alwaysFire;
                opts.ExceptionFactory = () => new Exception("deterministic chaos");
            })
            .Build();

        await Assert.ThrowsAsync<Exception>(() =>
            policy.ExecuteAsync<string>(_ => Task.FromResult("ok")));
    }

    // -----------------------------------------------------------------------
    // Void operation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Chaos_VoidOperation_NoInjection_Executes()
    {
        bool executed = false;
        var policy = ResiliencePolicy.Create()
            .Chaos(0.0)
            .Build();

        await policy.ExecuteAsync(async _ =>
        {
            executed = true;
            await Task.Yield();
        });

        Assert.True(executed);
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private sealed class DeterministicRandom : Random
    {
        private readonly double _value;
        public DeterministicRandom(double value) : base(0) => _value = value;
        public override double NextDouble() => _value;
    }
}
