using Vali_Mediator_Resilience.Core.Exceptions;
using Xunit;
using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Tests;

public class BulkheadPolicyTests
{
    // -----------------------------------------------------------------------
    // Basic concurrency limiting
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Bulkhead_BelowLimit_AllowsRequest()
    {
        var policy = ResiliencePolicy.Create()
            .Bulkhead(5)
            .Build();

        var result = await policy.ExecuteAsync(_ => Task.FromResult(1));

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Bulkhead_ConcurrentRequests_WithinLimit_AllPass()
    {
        var policy = ResiliencePolicy.Create()
            .Bulkhead(options =>
            {
                options.MaxConcurrentCalls = 5;
                options.MaxQueuedCalls = 0;
            })
            .Build();

        // Run 5 concurrent calls — all should pass
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            policy.ExecuteAsync(async ct =>
            {
                await Task.Delay(20, ct);
                return 1;
            })).ToList();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(1, r));
    }

    [Fact]
    public async Task Bulkhead_ExceedsLimit_NoQueue_ThrowsBulkheadRejectedException()
    {
        var policy = ResiliencePolicy.Create()
            .Bulkhead(options =>
            {
                options.MaxConcurrentCalls = 1;
                options.MaxQueuedCalls = 0; // no queue
            })
            .Build();

        var barrier = new SemaphoreSlim(0, 1);
        var slot = new SemaphoreSlim(0, 1);

        // Hold the one slot
        var occupying = policy.ExecuteAsync(async _ =>
        {
            slot.Release();
            await barrier.WaitAsync();
            return 1;
        });

        await slot.WaitAsync(); // wait until slot is occupied

        // This should be rejected immediately
        await Assert.ThrowsAsync<BulkheadRejectedException>(() =>
            policy.ExecuteAsync(_ => Task.FromResult(2)));

        barrier.Release();
        await occupying;
    }

    // -----------------------------------------------------------------------
    // Shorthand
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Bulkhead_Shorthand_MaxConcurrentAndQueued()
    {
        var policy = ResiliencePolicy.Create()
            .Bulkhead(10, 5)
            .Build();

        var result = await policy.ExecuteAsync(_ => Task.FromResult(99));
        Assert.Equal(99, result);
    }

    // -----------------------------------------------------------------------
    // OnRejected callback
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Bulkhead_OnRejectedCallback_InvokedWhenRejected()
    {
        bool callbackInvoked = false;

        var policy = ResiliencePolicy.Create()
            .Bulkhead(options =>
            {
                options.MaxConcurrentCalls = 1;
                options.MaxQueuedCalls = 0;
                options.OnRejected = _ =>
                {
                    callbackInvoked = true;
                    return Task.CompletedTask;
                };
            })
            .Build();

        var barrier = new SemaphoreSlim(0, 1);
        var slot = new SemaphoreSlim(0, 1);

        var occupying = policy.ExecuteAsync(async _ =>
        {
            slot.Release();
            await barrier.WaitAsync();
            return 1;
        });

        await slot.WaitAsync();

        try
        {
            await policy.ExecuteAsync(_ => Task.FromResult(2));
        }
        catch (BulkheadRejectedException) { }

        barrier.Release();
        await occupying;

        Assert.True(callbackInvoked);
    }

    // -----------------------------------------------------------------------
    // BulkheadRejectedException content
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Bulkhead_RejectedException_ContainsLimits()
    {
        var policy = ResiliencePolicy.Create()
            .Bulkhead(options =>
            {
                options.MaxConcurrentCalls = 2;
                options.MaxQueuedCalls = 0;
            })
            .Build();

        var barrier = new SemaphoreSlim(0, 2);
        var slots = new SemaphoreSlim(0, 2);

        // Fill both slots
        var t1 = policy.ExecuteAsync(async _ => { slots.Release(); await barrier.WaitAsync(); return 1; });
        var t2 = policy.ExecuteAsync(async _ => { slots.Release(); await barrier.WaitAsync(); return 1; });

        await slots.WaitAsync();
        await slots.WaitAsync();

        BulkheadRejectedException? ex = null;
        try
        {
            await policy.ExecuteAsync(_ => Task.FromResult(3));
        }
        catch (BulkheadRejectedException e)
        {
            ex = e;
        }

        barrier.Release(2);
        await Task.WhenAll(t1, t2);

        Assert.NotNull(ex);
        Assert.Equal(2, ex!.MaxConcurrentCalls);
        Assert.Equal(0, ex.MaxQueuedCalls);
    }

    // -----------------------------------------------------------------------
    // Void operation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Bulkhead_VoidOperation_CompletesSuccessfully()
    {
        var policy = ResiliencePolicy.Create()
            .Bulkhead(5)
            .Build();

        bool ran = false;
        await policy.ExecuteAsync(_ => { ran = true; return Task.CompletedTask; });
        Assert.True(ran);
    }
}
