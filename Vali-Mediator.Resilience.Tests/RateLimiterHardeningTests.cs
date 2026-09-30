using System.Diagnostics;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Pipeline;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class RateLimiterHardeningTests
{
    private static Task<bool> Acquire(RateLimiterState state) => state.TryAcquireAsync(CancellationToken.None);

    private static async Task Drain(RateLimiterState state, int count)
    {
        for (int i = 0; i < count; i++)
            Assert.True(await Acquire(state));
        Assert.False(await Acquire(state));
    }

    [Fact]
    public async Task TokenBucket_HugeIdleTime_RefillsToCapacityWithoutOverflow()
    {
        var clock = new ManualClock();
        var state = new RateLimiterState(new RateLimiterOptions
        {
            BucketCapacity = 10,
            TokensPerInterval = 1000,
            ReplenishmentInterval = TimeSpan.FromMilliseconds(1)
        }, clock.Read);
        await Drain(state, 10);

        clock.Advance(TimeSpan.FromDays(3650)); // 10 years of 1 ms intervals used to overflow int math

        for (int i = 0; i < 10; i++)
            Assert.True(await Acquire(state));
        Assert.False(await Acquire(state));
    }

    [Fact]
    public async Task TokenBucket_ClockMovingBackwards_DoesNotStallTheRefill()
    {
        var clock = new ManualClock();
        var state = new RateLimiterState(new RateLimiterOptions
        {
            BucketCapacity = 1,
            TokensPerInterval = 1,
            ReplenishmentInterval = TimeSpan.FromSeconds(1)
        }, clock.Read);
        await Drain(state, 1);

        clock.Advance(TimeSpan.FromHours(-1));
        Assert.False(await Acquire(state));   // rebases on the earlier time
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(await Acquire(state));
    }

    [Fact]
    public async Task TokenBucket_ZeroTokensPerInterval_NeverRefills()
    {
        var clock = new ManualClock();
        var state = new RateLimiterState(new RateLimiterOptions { BucketCapacity = 1, TokensPerInterval = 0 }, clock.Read);
        await Drain(state, 1);

        clock.Advance(TimeSpan.FromDays(30));

        Assert.False(await Acquire(state));
    }

    [Fact]
    public async Task SlidingWindow_PermitsAgainOnceOldCallsLeaveTheWindow()
    {
        var clock = new ManualClock();
        var state = new RateLimiterState(new RateLimiterOptions
        {
            Algorithm = RateLimiterAlgorithm.SlidingWindow,
            PermitLimit = 2,
            Window = TimeSpan.FromSeconds(10)
        }, clock.Read);
        await Drain(state, 2);

        clock.Advance(TimeSpan.FromSeconds(11));

        Assert.True(await Acquire(state));
    }

    [Fact]
    public void InvalidOptions_AreRejectedOnConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RateLimiterState(new RateLimiterOptions { ReplenishmentInterval = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RateLimiterState(new RateLimiterOptions { Algorithm = RateLimiterAlgorithm.SlidingWindow, Window = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RateLimiterState(new RateLimiterOptions { BucketCapacity = -1 }));
    }

    // -----------------------------------------------------------------------
    // Waiting for a permit (QueueTimeout): no polling, exact deadline
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QueueTimeout_WaitsForTheNextReplenishmentThenAcquires()
    {
        var state = new RateLimiterState(new RateLimiterOptions
        {
            BucketCapacity = 1,
            TokensPerInterval = 1,
            ReplenishmentInterval = TimeSpan.FromMilliseconds(200),
            QueueTimeout = TimeSpan.FromSeconds(10)
        });
        Assert.True(await Acquire(state));

        var sw = Stopwatch.StartNew();
        bool second = await Acquire(state);
        sw.Stop();

        Assert.True(second);
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(100), $"acquired after only {sw.Elapsed}");
    }

    [Fact]
    public async Task QueueTimeout_GivesUpAtTheDeadlineWhenNoPermitComes()
    {
        var state = new RateLimiterState(new RateLimiterOptions
        {
            BucketCapacity = 1,
            TokensPerInterval = 1,
            ReplenishmentInterval = TimeSpan.FromHours(1),
            QueueTimeout = TimeSpan.FromMilliseconds(200)
        });
        Assert.True(await Acquire(state));

        var sw = Stopwatch.StartNew();
        bool second = await Acquire(state);
        sw.Stop();

        Assert.False(second);
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(150), $"gave up after only {sw.Elapsed}");
    }

    [Fact]
    public async Task QueueTimeout_WaitIsCancellable()
    {
        var state = new RateLimiterState(new RateLimiterOptions
        {
            BucketCapacity = 1,
            TokensPerInterval = 1,
            ReplenishmentInterval = TimeSpan.FromHours(1),
            QueueTimeout = TimeSpan.FromMinutes(5)
        });
        Assert.True(await Acquire(state));
        using var cts = new CancellationTokenSource();

        var waiting = state.TryAcquireAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    // -----------------------------------------------------------------------
    // Partition bound
    // -----------------------------------------------------------------------

    [Fact]
    public void Partitions_BeyondTheBound_ShareOneOverflowLimiter()
    {
        var clock = new ManualClock();
        var partitioned = new PartitionedRateLimiterState(new RateLimiterOptions { MaxPartitions = 3 }, clock.Read);

        var a = partitioned.GetOrCreate("a");
        partitioned.GetOrCreate("b");
        partitioned.GetOrCreate("c");
        var d = partitioned.GetOrCreate("d");
        var e = partitioned.GetOrCreate("e");

        Assert.Equal(3, partitioned.Count);
        Assert.Same(d, e);
        Assert.NotSame(a, d);
        Assert.Same(a, partitioned.GetOrCreate("a"));
    }

    [Fact]
    public async Task Partitions_OverflowLimiterIsShared_SoAFloodOfKeysCannotMultiplyPermits()
    {
        var clock = new ManualClock();
        var partitioned = new PartitionedRateLimiterState(
            new RateLimiterOptions { MaxPartitions = 1, BucketCapacity = 1, TokensPerInterval = 0 }, clock.Read);
        partitioned.GetOrCreate("owner");

        Assert.True(await Acquire(partitioned.GetOrCreate("flood-1")));
        Assert.False(await Acquire(partitioned.GetOrCreate("flood-2")));
        Assert.False(await Acquire(partitioned.GetOrCreate("flood-3")));
    }

    [Fact]
    public void Partitions_IdleOnesFreeTheirSlotsForNewKeys()
    {
        var clock = new ManualClock();
        var partitioned = new PartitionedRateLimiterState(
            new RateLimiterOptions { MaxPartitions = 2, PartitionIdleTimeout = TimeSpan.FromMinutes(1) }, clock.Read);
        var a = partitioned.GetOrCreate("a");
        partitioned.GetOrCreate("b");
        var overflow = partitioned.GetOrCreate("c");

        clock.Advance(TimeSpan.FromMinutes(30));
        var c = partitioned.GetOrCreate("c");

        Assert.NotSame(a, c);
        Assert.NotSame(overflow, c);
        Assert.Equal(1, partitioned.Count);
    }

    [Fact]
    public void Partitions_ConcurrentKeys_NeverExceedTheBound()
    {
        var partitioned = new PartitionedRateLimiterState(new RateLimiterOptions { MaxPartitions = 50 });

        Parallel.For(0, 5000, i => partitioned.GetOrCreate("key-" + (i % 400)));

        Assert.True(partitioned.Count <= 50, $"Count was {partitioned.Count}");
    }

    [Fact]
    public void MonotonicClock_NeverGoesBackwards()
    {
        var previous = MonotonicClock.Now;
        for (int i = 0; i < 10_000; i++)
        {
            var now = MonotonicClock.Now;
            Assert.True(now >= previous);
            previous = now;
        }
    }
}
