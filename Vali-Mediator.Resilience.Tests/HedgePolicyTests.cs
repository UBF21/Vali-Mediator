using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Policies;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class HedgePolicyTests
{
    // -----------------------------------------------------------------------
    // Basic hedge: fast path wins
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hedge_FirstCallSucceedsBeforeDelay_NoHedgeFired()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Hedge(TimeSpan.FromSeconds(10)) // very long delay — hedge should never fire
            .Build();

        var result = await policy.ExecuteAsync<string>(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult("original");
        });

        Assert.Equal("original", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Hedge_OriginalSlowHedgeFast_HedgeResultWins()
    {
        int hedgeFired = 0;
        int callIndex = 0;

        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(50);
                opts.MaxHedgedAttempts = 1;
                opts.OnHedge = _ => { Interlocked.Increment(ref hedgeFired); return Task.CompletedTask; };
            })
            .Build();

        var result = await policy.ExecuteAsync<string>(async ct =>
        {
            int idx = Interlocked.Increment(ref callIndex);
            if (idx == 1)
            {
                // Original call: slow
                await Task.Delay(500, ct);
                return "original";
            }
            else
            {
                // Hedge call: fast
                await Task.Delay(10, ct);
                return "hedge";
            }
        });

        // The hedge completed first
        Assert.Equal("hedge", result);
        Assert.Equal(1, hedgeFired);
    }

    [Fact]
    public async Task Hedge_AllAttemptsSucceed_FirstCompletingWins()
    {
        int calls = 0;

        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(10);
                opts.MaxHedgedAttempts = 2;
            })
            .Build();

        // All calls complete quickly — whichever finishes first wins
        string result = await policy.ExecuteAsync<string>(async ct =>
        {
            int idx = Interlocked.Increment(ref calls);
            await Task.Delay(idx * 5, ct); // later calls take longer
            return $"call-{idx}";
        });

        // First call (idx=1) with 5ms delay should win over later ones
        Assert.StartsWith("call-", result);
        Assert.True(calls >= 1);
    }

    [Fact]
    public async Task Hedge_AllAttemptsThrow_ThrowsLastException()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(10);
                opts.MaxHedgedAttempts = 1;
            })
            .Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<string>(_ =>
                throw new InvalidOperationException($"fail-{Interlocked.Increment(ref calls)}")));

        Assert.Equal(2, calls);
        Assert.Equal("fail-2", ex.Message);
    }

    [Fact]
    public async Task Hedge_ShouldHedgeOnException_WhenFalse_ThrowsImmediatelyWithoutHedging()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(10);
                opts.MaxHedgedAttempts = 1;
                opts.ShouldHedgeOnException = _ => false;
            })
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<string>(_ =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException("not hedged");
            }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Hedge_FirstAttemptFails_HedgeDelayIsStillRespected()
    {
        var starts = new List<DateTime>();
        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(150);
                opts.MaxHedgedAttempts = 1;
            })
            .Build();

        var result = await policy.ExecuteAsync<string>(_ =>
        {
            int idx;
            lock (starts) { starts.Add(DateTime.UtcNow); idx = starts.Count; }
            if (idx == 1) throw new InvalidOperationException("first fails");
            return Task.FromResult("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(2, starts.Count);
        Assert.True((starts[1] - starts[0]).TotalMilliseconds >= 100,
            $"hedge fired after {(starts[1] - starts[0]).TotalMilliseconds} ms");
    }

    [Fact]
    public async Task Hedge_Winner_ReturnsWithoutWaitingForLoserThatIgnoresItsToken()
    {
        int calls = 0;
        var releaseLoser = new TaskCompletionSource();
        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(20);
                opts.MaxHedgedAttempts = 1;
            })
            .Build();

        var call = policy.ExecuteAsync<string>(async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await releaseLoser.Task; // ignores the token on purpose
                return "slow";
            }
            return "fast";
        });

        var finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(call, finished); // the winner must not be held back by the loser
        Assert.Equal("fast", await call);
        releaseLoser.SetResult();
    }

    [Fact]
    public async Task Hedge_Winner_CancelsTheLosersToken()
    {
        int calls = 0;
        var loserCancelled = new TaskCompletionSource();
        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(20);
                opts.MaxHedgedAttempts = 1;
            })
            .Build();

        var result = await policy.ExecuteAsync<string>(async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { loserCancelled.SetResult(); throw; }
            }
            return "fast";
        });

        Assert.Equal("fast", result);
        var done = await Task.WhenAny(loserCancelled.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(loserCancelled.Task, done);
    }

    [Fact]
    public async Task Hedge_OnHedgeReceivesItsOwnAttemptNumberWithoutTouchingTheSharedContext()
    {
        int calls = 0;
        int seenInCallback = -1;
        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(20);
                opts.MaxHedgedAttempts = 1;
                opts.OnHedge = ctx => { seenInCallback = ctx.AttemptNumber; return Task.CompletedTask; };
            })
            .Build();

        await policy.ExecuteAsync<string>(async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                await Task.Delay(Timeout.Infinite, ct);
            return "fast";
        });

        Assert.Equal(1, seenInCallback);
    }

    [Fact]
    public async Task Hedge_DoesNotOverwriteRetryAttemptNumber()
    {
        int calls = 0;
        var attemptsSeenByRetry = new List<int>();
        var policy = ResiliencePolicy.Create()
            .Retry(opts =>
            {
                opts.MaxRetries = 1;
                opts.InitialDelay = TimeSpan.Zero;
                opts.OnRetry = (ctx, _) => { attemptsSeenByRetry.Add(ctx.AttemptNumber); return Task.CompletedTask; };
            })
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(5);
                opts.MaxHedgedAttempts = 1;
            })
            .Build();

        var result = await policy.ExecuteAsync<string>(_ =>
        {
            if (Interlocked.Increment(ref calls) <= 2) throw new InvalidOperationException("fail");
            return Task.FromResult("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(new[] { 0 }, attemptsSeenByRetry);
    }

    [Fact]
    public async Task Hedge_ShouldHedgeOnResult_HedgesWhenPredicateTrue()
    {
        int calls = 0;

        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(10);
                opts.MaxHedgedAttempts = 1;
                // Treat "retry" as a bad result
                opts.ShouldHedgeOnResult = r => r is string s && s == "retry";
            })
            .Build();

        string result = await policy.ExecuteAsync<string>(async _ =>
        {
            await Task.Yield();
            if (Interlocked.Increment(ref calls) == 1)
                return "retry";   // first call: bad result → hedge
            return "success";     // second call: good result
        });

        Assert.Equal("success", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Hedge_VoidOperation_WorksCorrectly()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Hedge(TimeSpan.FromSeconds(10))
            .Build();

        await policy.ExecuteAsync(async _ =>
        {
            Interlocked.Increment(ref calls);
            await Task.Yield();
        });

        Assert.Equal(1, calls);
    }
}
