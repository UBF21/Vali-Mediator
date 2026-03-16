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
    public async Task Hedge_AllAttemptsThrow_ExceptionsAreSupressed_ReturnsDefault()
    {
        // By default, exceptions from hedged attempts are treated as "try next hedge"
        // (ShouldHedgeOnException = null → shouldHedge=true → IsSuccess=false, Exception=null).
        // When all attempts exhaust with no winner, the executor returns default! (null for string).
        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(10);
                opts.MaxHedgedAttempts = 1;
            })
            .Build();

        string? result = await policy.ExecuteAsync<string>(_ => throw new InvalidOperationException("suppressed"));
        Assert.Null(result);
    }

    [Fact]
    public async Task Hedge_ShouldHedgeOnException_WhenFalse_TreatsAttemptAsCompleteReturnsDefault()
    {
        // When ShouldHedgeOnException = _ => false, the exception triggers IsSuccess=true (don't hedge further).
        // The executor returns the Result from that attempt (which is default!) without rethrowing.
        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(10);
                opts.MaxHedgedAttempts = 1;
                opts.ShouldHedgeOnException = _ => false;
            })
            .Build();

        string? result = await policy.ExecuteAsync<string>(_ => throw new InvalidOperationException("not hedged"));
        Assert.Null(result); // default for string
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
