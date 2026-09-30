using Vali_Mediator_Resilience.Core.Policies;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

[CollectionDefinition("Serial", DisableParallelization = true)]
public sealed class SerialCollection { }

/// <summary>Measures process-wide allocations, so it must not run alongside other tests.</summary>
[Collection("Serial")]
public class HedgeSpinTests
{
    [Fact]
    public async Task Hedge_MaxAttemptsReachedWithAttemptInFlight_WaitsWithoutSpinning()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Hedge(opts =>
            {
                opts.HedgeDelay = TimeSpan.FromMilliseconds(20);
                opts.MaxHedgedAttempts = 1;
            })
            .Build();

        long before = GC.GetTotalAllocatedBytes(false);
        var result = await policy.ExecuteAsync<string>(async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("first fails");
            await Task.Delay(400, ct);
            return "slow-hedge";
        });
        long allocated = GC.GetTotalAllocatedBytes(false) - before;

        Assert.Equal("slow-hedge", result);
        Assert.Equal(2, calls);
        // A busy loop over 400 ms allocates hundreds of MB; an idle wait allocates a few KB.
        Assert.True(allocated < 20_000_000, $"allocated {allocated} bytes while waiting");
    }
}
