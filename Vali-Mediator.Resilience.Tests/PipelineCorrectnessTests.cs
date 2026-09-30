using System.Diagnostics;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Policies;
using Vali_Mediator_Resilience.Core.Registry;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class PipelineCorrectnessTests
{
    private static Task<int> Blocked(TaskCompletionSource<bool> gate) =>
        gate.Task.ContinueWith(_ => 1, TaskScheduler.Default);

    private static TaskCompletionSource<bool> NewGate() =>
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task OpenCircuit(ResiliencePolicy policy)
    {
        try { await policy.ExecuteAsync<int>(_ => throw new InvalidOperationException("boom")); }
        catch (InvalidOperationException) { }
    }

    // -----------------------------------------------------------------------
    // M-06 — order: Fallback → Chaos → RateLimiter → Retry → Timeout → CB → Bulkhead → Hedge
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Order_RateLimiterIsOutsideRetry_OneTokenPerLogicalCall()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .RateLimiter(o =>
            {
                o.BucketCapacity = 2;
                o.TokensPerInterval = 1;
                o.ReplenishmentInterval = TimeSpan.FromHours(1);
            })
            .Retry(o =>
            {
                o.MaxRetries = 2;
                o.BackoffType = BackoffType.Fixed;
                o.InitialDelay = TimeSpan.Zero;
            })
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ => { calls++; throw new InvalidOperationException("fail"); }));

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Order_RateLimiterRejection_DoesNotCountAsCircuitBreakerFailure()
    {
        var registry = new CircuitBreakerRegistry();
        var policy = ResiliencePolicy.Create()
            .RateLimiter(o =>
            {
                o.BucketCapacity = 1;
                o.TokensPerInterval = 1;
                o.ReplenishmentInterval = TimeSpan.FromHours(1);
            })
            .CircuitBreaker(o =>
            {
                o.CircuitKey = "order-rl-cb";
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            })
            .UseRegistry(registry)
            .Build();

        await policy.ExecuteAsync(_ => Task.FromResult(1));
        await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            policy.ExecuteAsync(_ => Task.FromResult(1)));

        Assert.Equal(CircuitState.Closed, registry.GetState("order-rl-cb"));
    }

    [Fact]
    public async Task Order_ChaosIsOutsideRetry_InjectedOncePerLogicalCall()
    {
        int injected = 0;
        var policy = ResiliencePolicy.Create()
            .Chaos(o =>
            {
                o.InjectionRate = 1.0;
                o.ExceptionFactory = () => new InvalidOperationException("chaos");
                o.OnChaosInjected = () => { injected++; return Task.CompletedTask; };
            })
            .Retry(o =>
            {
                o.MaxRetries = 3;
                o.BackoffType = BackoffType.Fixed;
                o.InitialDelay = TimeSpan.Zero;
            })
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(_ => Task.FromResult(1)));

        Assert.Equal(1, injected);
    }

    // -----------------------------------------------------------------------
    // M-07 — Fallback must not swallow caller cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Fallback_CallerCancellation_DoesNotActivateFallback()
    {
        var policy = ResiliencePolicy.Create()
            .Fallback<int>(o => o.FallbackValue = -1);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync(async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return 1;
            }, cts.Token));
    }

    // -----------------------------------------------------------------------
    // M-08 — Bulkhead queue semantics
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Bulkhead_QueueGreaterThanZero_InfiniteTimeout_WaitsForSlot()
    {
        var policy = ResiliencePolicy.Create().Bulkhead(1, 1).Build();
        var gate = NewGate();

        var first = policy.ExecuteAsync(_ => Blocked(gate));
        var second = policy.ExecuteAsync(_ => Task.FromResult(2));

        Assert.False(second.IsCompleted);

        gate.SetResult(true);
        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
    }

    [Fact]
    public async Task Bulkhead_QueueFull_RejectsExtraCall()
    {
        var policy = ResiliencePolicy.Create().Bulkhead(1, 1).Build();
        var gate = NewGate();

        var first = policy.ExecuteAsync(_ => Blocked(gate));
        var queued = policy.ExecuteAsync(_ => Task.FromResult(2)); // already queued: ExecuteAsync runs synchronously up to the wait

        await Assert.ThrowsAsync<BulkheadRejectedException>(() =>
            policy.ExecuteAsync(_ => Task.FromResult(3)));

        gate.SetResult(true);
        await first;
        await queued;
    }

    [Fact]
    public async Task Bulkhead_NoQueue_FiniteTimeout_RejectsImmediately()
    {
        var policy = ResiliencePolicy.Create()
            .Bulkhead(o =>
            {
                o.MaxConcurrentCalls = 1;
                o.MaxQueuedCalls = 0;
                o.QueueTimeout = TimeSpan.FromSeconds(30);
            })
            .Build();
        var gate = NewGate();
        var first = policy.ExecuteAsync(_ => Blocked(gate));

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<BulkheadRejectedException>(() =>
            policy.ExecuteAsync(_ => Task.FromResult(2)));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Rejection took {sw.Elapsed}");
        gate.SetResult(true);
        await first;
    }

    // -----------------------------------------------------------------------
    // M-09 / M-10 / M-12 / M-13 — Circuit breaker
    // -----------------------------------------------------------------------

    private static ResiliencePolicy Cb(string key, ICircuitBreakerRegistry registry,
        int threshold = 1, int halfOpenAttempts = 1,
        Func<Vali_Mediator_Resilience.Core.Context.ResilienceContext, Task>? onHalfOpen = null,
        Func<Vali_Mediator_Resilience.Core.Context.ResilienceContext, Task>? onClose = null,
        Action<ResiliencePolicyBuilder>? extra = null)
    {
        var builder = ResiliencePolicy.Create(key)
            .CircuitBreaker(o =>
            {
                o.CircuitKey = key;
                o.FailureThreshold = threshold;
                o.SamplingDuration = TimeSpan.FromSeconds(60);
                o.BreakDuration = TimeSpan.FromMilliseconds(50);
                o.HalfOpenMaxAttempts = halfOpenAttempts;
                o.OnHalfOpen = onHalfOpen;
                o.OnClose = onClose;
            })
            .UseRegistry(registry);
        extra?.Invoke(builder);
        return builder.Build();
    }

    [Fact]
    public async Task CircuitBreaker_HalfOpen_AdmitsExactlyHalfOpenMaxAttempts()
    {
        var clock = new ManualClock();
        var registry = new ClockedRegistry(clock);
        var policy = Cb("cb-halfopen-exact", registry, halfOpenAttempts: 2);
        await OpenCircuit(policy);
        clock.Advance(TimeSpan.FromMilliseconds(120));
        var gate = NewGate();

        var probe1 = policy.ExecuteAsync(_ => Blocked(gate));
        var probe2 = policy.ExecuteAsync(_ => Blocked(gate));
        var probe3 = policy.ExecuteAsync(_ => Blocked(gate));

        await Assert.ThrowsAsync<CircuitOpenException>(() => probe3.WaitAsync(TimeSpan.FromSeconds(3)));
        gate.SetResult(true);
        await probe1;
        await probe2;
    }

    [Fact]
    public async Task CircuitBreaker_ClosingFromHalfOpen_ClearsFailureWindow()
    {
        var clock = new ManualClock();
        var registry = new ClockedRegistry(clock);
        var policy = Cb("cb-window-clear", registry, threshold: 2);
        await OpenCircuit(policy);
        await OpenCircuit(policy);
        Assert.Equal(CircuitState.Open, registry.GetState("cb-window-clear"));

        clock.Advance(TimeSpan.FromMilliseconds(120));
        await policy.ExecuteAsync(_ => Task.FromResult(1));
        Assert.Equal(CircuitState.Closed, registry.GetState("cb-window-clear"));

        await OpenCircuit(policy);

        Assert.Equal(CircuitState.Closed, registry.GetState("cb-window-clear"));
    }

    [Fact]
    public async Task CircuitBreaker_Callbacks_FireOncePerTransition()
    {
        int halfOpen = 0, closed = 0;
        var clock = new ManualClock();
        var registry = new ClockedRegistry(clock);
        var policy = Cb("cb-callbacks", registry,
            onHalfOpen: _ => { halfOpen++; return Task.CompletedTask; },
            onClose: _ => { closed++; return Task.CompletedTask; });
        await OpenCircuit(policy);
        clock.Advance(TimeSpan.FromMilliseconds(120));

        for (int i = 0; i < 4; i++)
            await policy.ExecuteAsync(_ => Task.FromResult(1));

        Assert.Equal(1, halfOpen);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task CircuitBreaker_CallerCancellation_DoesNotCountAsFailure()
    {
        var registry = new CircuitBreakerRegistry();
        var policy = Cb("cb-cancel", registry);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(30);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync<int>(async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return 1;
            }, cancellationToken: cts.Token));

        Assert.Equal(CircuitState.Closed, registry.GetState("cb-cancel"));
    }

    [Fact]
    public async Task CircuitBreaker_BulkheadRejection_DoesNotCountAsFailure()
    {
        var registry = new CircuitBreakerRegistry();
        var policy = Cb("cb-bulkhead", registry, extra: b => b.Bulkhead(1));
        var gate = NewGate();
        var first = policy.ExecuteAsync(_ => Blocked(gate));

        await Assert.ThrowsAsync<BulkheadRejectedException>(() =>
            policy.ExecuteAsync(_ => Task.FromResult(2)));

        Assert.Equal(CircuitState.Closed, registry.GetState("cb-bulkhead"));
        gate.SetResult(true);
        await first;
    }

    [Fact]
    public async Task CircuitBreaker_CancelledHalfOpenProbe_ReleasesProbeSlot()
    {
        var clock = new ManualClock();
        var registry = new ClockedRegistry(clock);
        var policy = Cb("cb-probe-release", registry);
        await OpenCircuit(policy);
        clock.Advance(TimeSpan.FromMilliseconds(120));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(30);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync<int>(async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return 1;
            }, cancellationToken: cts.Token));
        Assert.Equal(CircuitState.HalfOpen, registry.GetState("cb-probe-release"));

        var result = await policy.ExecuteAsync(_ => Task.FromResult(7));

        Assert.Equal(7, result);
        Assert.Equal(CircuitState.Closed, registry.GetState("cb-probe-release"));
    }

    // -----------------------------------------------------------------------
    // B-07 — exponential backoff must not overflow TimeSpan
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(BackoffType.Exponential)]
    [InlineData(BackoffType.ExponentialWithJitter)]
    public async Task Retry_ExponentialBackoff_ManyRetries_DoesNotOverflow(BackoffType backoff)
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Retry(o =>
            {
                o.MaxRetries = 20;
                o.BackoffType = backoff;
                o.InitialDelay = TimeSpan.FromMilliseconds(1);
                o.MaxDelay = TimeSpan.FromMilliseconds(1);
                o.Multiplier = 10;
            })
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ => { calls++; throw new InvalidOperationException("fail"); }));

        Assert.Equal(21, calls);
    }
}
