using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Pipeline;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class RateLimiterStateTests
{
    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Read() => Now;
    }

    [Fact]
    public async Task TokenBucket_KeepsFractionalIntervalsWhenReplenishing()
    {
        var clock = new FakeClock();
        var state = new RateLimiterState(new RateLimiterOptions
        {
            BucketCapacity = 10,
            TokensPerInterval = 1,
            ReplenishmentInterval = TimeSpan.FromSeconds(1)
        }, clock.Read);

        for (int i = 0; i < 10; i++)
            Assert.True(await state.TryAcquireAsync(CancellationToken.None));
        Assert.False(await state.TryAcquireAsync(CancellationToken.None));

        clock.Now += TimeSpan.FromMilliseconds(1500);
        Assert.True(await state.TryAcquireAsync(CancellationToken.None));   // 1 token, 0.5 s left over
        Assert.False(await state.TryAcquireAsync(CancellationToken.None));

        clock.Now += TimeSpan.FromMilliseconds(500);                        // leftover + 0.5 s = 1 token
        Assert.True(await state.TryAcquireAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TokenBucket_DoesNotBankTimeWhileBucketIsFull()
    {
        var clock = new FakeClock();
        var state = new RateLimiterState(new RateLimiterOptions
        {
            BucketCapacity = 2,
            TokensPerInterval = 1,
            ReplenishmentInterval = TimeSpan.FromSeconds(1)
        }, clock.Read);

        clock.Now += TimeSpan.FromSeconds(100);
        Assert.True(await state.TryAcquireAsync(CancellationToken.None));
        Assert.True(await state.TryAcquireAsync(CancellationToken.None));
        Assert.False(await state.TryAcquireAsync(CancellationToken.None));
    }

    [Fact]
    public void Partitions_IdleOnesAreEvicted()
    {
        var clock = new FakeClock();
        var options = new RateLimiterOptions { PartitionIdleTimeout = TimeSpan.FromMinutes(1) };
        var partitioned = new PartitionedRateLimiterState(options, clock.Read);

        for (int i = 0; i < 1000; i++)
            partitioned.GetOrCreate("user-" + i);
        Assert.Equal(1000, partitioned.Count);

        clock.Now += TimeSpan.FromMinutes(10);
        partitioned.GetOrCreate("active");

        Assert.Equal(1, partitioned.Count);
    }

    [Fact]
    public void Partitions_RecentlyUsedOnesSurviveTheSweep()
    {
        var clock = new FakeClock();
        var options = new RateLimiterOptions { PartitionIdleTimeout = TimeSpan.FromMinutes(1) };
        var partitioned = new PartitionedRateLimiterState(options, clock.Read);

        var first = partitioned.GetOrCreate("a");
        clock.Now += TimeSpan.FromSeconds(50);
        partitioned.GetOrCreate("a");
        clock.Now += TimeSpan.FromSeconds(50);
        partitioned.GetOrCreate("b");

        Assert.Same(first, partitioned.GetOrCreate("a"));
    }
}
