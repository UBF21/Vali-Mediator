using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Vali_Mediator_Caching.Core.Abstractions;
using Vali_Mediator_Caching.Core.Enums;
using Vali_Mediator_Caching.Core.Interfaces;
using Vali_Mediator_Caching.Core.Store;
using Vali_Mediator_Caching.Extension;
using Vali_Mediator_Caching.Pipeline;
using Xunit;

namespace Vali_Mediator_Caching.Tests;

internal sealed class CoalescedQuery : IRequest<Result<string>>, ICacheable
{
    public string CacheKey { get; init; } = "coalesced";
    public TimeSpan? AbsoluteExpiration => null;
    public TimeSpan? SlidingExpiration => null;
    public string? CacheGroup => null;
    public bool BypassCache => false;
    public CacheOrder Order => CacheOrder.ReadThenWrite;
}

public sealed class CoalescingTests
{
    private static int Waiting => CachingBehavior<CoalescedQuery, Result<string>>.WaitingCount;

    private static Task<Result<string>> Run(
        CachingBehavior<CoalescedQuery, Result<string>> behavior,
        string key,
        Func<CancellationToken, Task<Result<string>>> next,
        CancellationToken ct = default)
        => Task.Run(() => behavior.Handle(new CoalescedQuery { CacheKey = key }, next, ct));

    [Fact]
    public async Task FailedResult_IsSharedWithWaiters_AndNotCached()
    {
        var behavior = new CachingBehavior<CoalescedQuery, Result<string>>(new InMemoryCacheStore());
        var calls = 0;
        var entered = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        Func<CancellationToken, Task<Result<string>>> next = async _ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await gate.Task;
            return Result<string>.Fail("nope", ErrorType.NotFound);
        };

        var leader = Run(behavior, "shared-fail", next);
        await entered.Task;
        var waiters = Enumerable.Range(0, 5).Select(_ => Run(behavior, "shared-fail", next)).ToArray();
        Assert.True(SpinWait.SpinUntil(() => Waiting == 5, TimeSpan.FromSeconds(10)));

        gate.SetResult();
        var results = await Task.WhenAll(waiters.Prepend(leader));

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.True(r.IsFailure));

        // The failure was not cached: a later call runs the handler again.
        await behavior.Handle(new CoalescedQuery { CacheKey = "shared-fail" },
            _ => { Interlocked.Increment(ref calls); return Task.FromResult(Result<string>.Ok("v")); },
            CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task HungLeader_DoesNotBlockWaitersPastTheTimeout()
    {
        var behavior = new CachingBehavior<CoalescedQuery, Result<string>>(
            new InMemoryCacheStore(),
            new CachingOptions { CoalescingWaitTimeout = TimeSpan.FromMilliseconds(100) });
        var entered = new TaskCompletionSource();
        var neverReleased = new TaskCompletionSource();

        var leader = Run(behavior, "hung", async _ =>
        {
            entered.TrySetResult();
            await neverReleased.Task;
            return Result<string>.Ok("late");
        });
        await entered.Task;

        var waiter = Run(behavior, "hung", _ => Task.FromResult(Result<string>.Ok("own")));

        var completed = await Task.WhenAny(waiter, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(waiter, completed);
        Assert.Equal("own", (await waiter).Value);

        neverReleased.SetResult();
        await leader;
    }

    [Fact]
    public async Task CancelledLeader_WaiterRetriesWithItsOwnExecution()
    {
        var behavior = new CachingBehavior<CoalescedQuery, Result<string>>(new InMemoryCacheStore());
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource();

        var leader = Run(behavior, "cancel", async ct =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Result<string>.Ok("unreachable");
        }, cts.Token);
        await entered.Task;

        var waiter = Run(behavior, "cancel", _ => Task.FromResult(Result<string>.Ok("second")));
        Assert.True(SpinWait.SpinUntil(() => Waiting == 1, TimeSpan.FromSeconds(10)));

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leader);
        Assert.Equal("second", (await waiter).Value);
        Assert.Equal(0, CachingBehavior<CoalescedQuery, Result<string>>.PendingLockCount);
    }

    [Fact]
    public async Task LeaderException_IsPropagatedToWaiters()
    {
        var behavior = new CachingBehavior<CoalescedQuery, Result<string>>(new InMemoryCacheStore());
        var calls = 0;
        var entered = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        Func<CancellationToken, Task<Result<string>>> next = async _ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await gate.Task;
            throw new InvalidOperationException("boom");
        };

        var leader = Run(behavior, "throws", next);
        await entered.Task;
        var waiter = Run(behavior, "throws", next);
        Assert.True(SpinWait.SpinUntil(() => Waiting == 1, TimeSpan.FromSeconds(10)));

        gate.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => leader);
        await Assert.ThrowsAsync<InvalidOperationException>(() => waiter);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task WaiterCancellation_DoesNotAffectLeader()
    {
        var behavior = new CachingBehavior<CoalescedQuery, Result<string>>(new InMemoryCacheStore());
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        Func<CancellationToken, Task<Result<string>>> next = async _ =>
        {
            entered.TrySetResult();
            await gate.Task;
            return Result<string>.Ok("v");
        };

        var leader = Run(behavior, "waiter-cancel", next);
        await entered.Task;
        var waiter = Run(behavior, "waiter-cancel", next, cts.Token);
        Assert.True(SpinWait.SpinUntil(() => Waiting == 1, TimeSpan.FromSeconds(10)));

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);

        gate.SetResult();
        Assert.Equal("v", (await leader).Value);
    }

    // -------------------------------------------------------------------------
    // Options
    // -------------------------------------------------------------------------

    [Fact]
    public void CachingOptions_DefaultsAndValidation()
    {
        var o = new CachingOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), o.CoalescingWaitTimeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => o.CoalescingWaitTimeout = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => o.CoalescingWaitTimeout = TimeSpan.FromSeconds(-1));

        o.CoalescingWaitTimeout = Timeout.InfiniteTimeSpan;
        Assert.Equal(Timeout.InfiniteTimeSpan, o.CoalescingWaitTimeout);
    }

    [Fact]
    public void Behavior_ResolvesFromDi_WithAndWithoutOptions()
    {
        var withoutOptions = new ServiceCollection()
            .AddInMemoryCacheStore()
            .AddTransient<CachingBehavior<CoalescedQuery, Result<string>>>()
            .BuildServiceProvider();
        Assert.NotNull(withoutOptions.GetRequiredService<CachingBehavior<CoalescedQuery, Result<string>>>());

        var withOptions = new ServiceCollection()
            .AddInMemoryCacheStore()
            .AddCachingOptions(o => o.CoalescingWaitTimeout = TimeSpan.FromSeconds(5))
            .AddTransient<CachingBehavior<CoalescedQuery, Result<string>>>()
            .BuildServiceProvider();
        Assert.NotNull(withOptions.GetRequiredService<CachingBehavior<CoalescedQuery, Result<string>>>());
    }
}
