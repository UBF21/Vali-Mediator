using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Policies;
using Vali_Mediator_Resilience.Core.Registry;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class CircuitBreakerHardeningTests
{
    private static readonly TimeSpan Break = TimeSpan.FromSeconds(30);

    private static CircuitBreakerState NewState(ManualClock clock, int threshold = 2, int halfOpenAttempts = 1)
        => new CircuitBreakerState(new CircuitBreakerOptions
        {
            CircuitKey = "k",
            FailureThreshold = threshold,
            SamplingDuration = TimeSpan.FromMinutes(10),
            BreakDuration = Break,
            HalfOpenMaxAttempts = halfOpenAttempts
        }, clock.Read);

    private static void Open(CircuitBreakerState state, int failures = 2)
    {
        for (int i = 0; i < failures; i++)
        {
            Assert.True(state.TryEnter(out int epoch, out _));
            state.RecordFailureAndCheckOpened(epoch);
        }

        Assert.Equal(CircuitState.Open, state.State);
    }

    [Fact]
    public void HalfOpen_AdmitsExactlyHalfOpenMaxAttempts()
    {
        var clock = new ManualClock();
        var state = NewState(clock, halfOpenAttempts: 2);
        Open(state);
        clock.Advance(Break);

        Assert.True(state.TryEnter(out _, out bool enteredHalfOpen));
        Assert.True(enteredHalfOpen);
        Assert.True(state.TryEnter(out _, out _));
        Assert.False(state.TryEnter(out _, out _));
    }

    [Fact]
    public void HalfOpen_ProbeThatNeverReports_DoesNotWedgeTheCircuit()
    {
        var clock = new ManualClock();
        var state = NewState(clock);
        Open(state);
        clock.Advance(Break);
        Assert.True(state.TryEnter(out _, out _));   // probe that is then lost
        Assert.False(state.TryEnter(out _, out _));

        clock.Advance(Break);

        Assert.True(state.TryEnter(out _, out _));   // a fresh probe is admitted
    }

    [Fact]
    public void LateSuccessFromEarlierEpoch_DoesNotCloseHalfOpenCircuit()
    {
        var clock = new ManualClock();
        var state = NewState(clock);
        Assert.True(state.TryEnter(out int slowCallEpoch, out _)); // slow call started while Closed
        Open(state);
        clock.Advance(Break);
        Assert.True(state.TryEnter(out int probeEpoch, out _));
        Assert.Equal(CircuitState.HalfOpen, state.State);

        Assert.False(state.RecordSuccessAndCheckClosed(slowCallEpoch));

        Assert.Equal(CircuitState.HalfOpen, state.State);
        Assert.True(state.RecordSuccessAndCheckClosed(probeEpoch));
        Assert.Equal(CircuitState.Closed, state.State);
    }

    [Fact]
    public void LateFailureFromEarlierEpoch_DoesNotReopenHalfOpenCircuit()
    {
        var clock = new ManualClock();
        var state = NewState(clock);
        Assert.True(state.TryEnter(out int slowCallEpoch, out _));
        Open(state);
        clock.Advance(Break);
        Assert.True(state.TryEnter(out int probeEpoch, out _));

        Assert.False(state.RecordFailureAndCheckOpened(slowCallEpoch));

        Assert.Equal(CircuitState.HalfOpen, state.State);
        Assert.True(state.RecordSuccessAndCheckClosed(probeEpoch));
    }

    [Fact]
    public void ReleaseProbe_FromPreviousCycle_DoesNotFreeTheCurrentProbe()
    {
        var clock = new ManualClock();
        var state = NewState(clock);
        Open(state);
        clock.Advance(Break);
        Assert.True(state.TryEnter(out int firstCycleEpoch, out _));
        Assert.True(state.RecordFailureAndCheckOpened(firstCycleEpoch)); // probe fails → Open again
        clock.Advance(Break);
        Assert.True(state.TryEnter(out _, out _));                        // second cycle's probe

        state.ReleaseProbe(firstCycleEpoch);                              // stale release must be ignored

        Assert.False(state.TryEnter(out _, out _));
    }

    [Fact]
    public void ConcurrentCallers_LeaveTheBreakerInAConsistentState()
    {
        var clock = new ManualClock();
        var state = NewState(clock, threshold: 5, halfOpenAttempts: 3);

        Parallel.For(0, 20_000, i =>
        {
            if (!state.TryEnter(out int epoch, out _)) return;
            switch (i % 3)
            {
                case 0: state.RecordFailureAndCheckOpened(epoch); break;
                case 1: state.RecordSuccessAndCheckClosed(epoch); break;
                default: state.ReleaseProbe(epoch); break;
            }

            if (i % 500 == 0) clock.Advance(Break);
        });

        Assert.True(Enum.IsDefined(typeof(CircuitState), state.State));
        Assert.True(state.RetryAfter() >= TimeSpan.Zero);
    }

    // -----------------------------------------------------------------------
    // Through the pipeline
    // -----------------------------------------------------------------------

    private static ResiliencePolicy Policy(ClockedRegistry registry, Action<CircuitBreakerOptions>? tweak = null)
        => ResiliencePolicy.Create("hard")
            .CircuitBreaker(o =>
            {
                o.CircuitKey = "hard";
                o.FailureThreshold = 1;
                o.SamplingDuration = TimeSpan.FromMinutes(10);
                o.BreakDuration = Break;
                tweak?.Invoke(o);
            })
            .UseRegistry(registry)
            .Build();

    private static async Task Trip(ResiliencePolicy policy)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task OnHalfOpenThrowing_ReleasesTheProbe()
    {
        var clock = new ManualClock();
        var registry = new ClockedRegistry(clock);
        bool throwInCallback = true;
        var policy = Policy(registry, o => o.OnHalfOpen = _ =>
            throwInCallback ? throw new ApplicationException("callback") : Task.CompletedTask);
        await Trip(policy);
        clock.Advance(Break);

        await Assert.ThrowsAsync<ApplicationException>(() => policy.ExecuteAsync(_ => Task.FromResult(1)));
        throwInCallback = false;

        Assert.Equal(CircuitState.HalfOpen, registry.GetState("hard"));
        Assert.Equal(1, await policy.ExecuteAsync(_ => Task.FromResult(1))); // probe slot was given back
        Assert.Equal(CircuitState.Closed, registry.GetState("hard"));
    }

    [Fact]
    public async Task NestedCircuitOpen_InsideAProbe_ReleasesItWithoutCountingAFailure()
    {
        var clock = new ManualClock();
        var registry = new ClockedRegistry(clock);
        var policy = Policy(registry);
        await Trip(policy);
        clock.Advance(Break);

        await Assert.ThrowsAsync<CircuitOpenException>(() =>
            policy.ExecuteAsync<int>(_ => throw new CircuitOpenException("other", TimeSpan.FromSeconds(1))));

        Assert.Equal(CircuitState.HalfOpen, registry.GetState("hard"));
        Assert.Equal(1, await policy.ExecuteAsync(_ => Task.FromResult(1)));
        Assert.Equal(CircuitState.Closed, registry.GetState("hard"));
    }

    [Fact]
    public async Task Callbacks_FireOncePerTransition()
    {
        var clock = new ManualClock();
        var registry = new ClockedRegistry(clock);
        int opened = 0, halfOpened = 0, closed = 0;
        var policy = Policy(registry, o =>
        {
            o.OnOpen = (_, _) => { opened++; return Task.CompletedTask; };
            o.OnHalfOpen = _ => { halfOpened++; return Task.CompletedTask; };
            o.OnClose = _ => { closed++; return Task.CompletedTask; };
        });

        await Trip(policy);
        clock.Advance(Break);
        for (int i = 0; i < 5; i++)
            await policy.ExecuteAsync(_ => Task.FromResult(1));

        Assert.Equal((1, 1, 1), (opened, halfOpened, closed));
    }
}
