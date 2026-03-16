using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Pipeline;
using Vali_Mediator_Resilience.Core.Policies;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class RateLimiterPolicyTests
{
    // -----------------------------------------------------------------------
    // Token Bucket
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TokenBucket_AllowsCallsUpToCapacity()
    {
        int capacity = 5;
        var policy = ResiliencePolicy.Create()
            .RateLimiter(opts =>
            {
                opts.Algorithm = RateLimiterAlgorithm.TokenBucket;
                opts.BucketCapacity = capacity;
                opts.TokensPerInterval = 0; // no replenishment during test
                opts.ReplenishmentInterval = TimeSpan.FromHours(1);
            })
            .Build();

        for (int i = 0; i < capacity; i++)
        {
            var result = await policy.ExecuteAsync<int>(_ => Task.FromResult(i));
            Assert.Equal(i, result);
        }
    }

    [Fact]
    public async Task TokenBucket_RejectsWhenBucketEmpty()
    {
        var policy = ResiliencePolicy.Create()
            .RateLimiter(opts =>
            {
                opts.Algorithm = RateLimiterAlgorithm.TokenBucket;
                opts.BucketCapacity = 1;
                opts.TokensPerInterval = 0; // no replenishment
                opts.ReplenishmentInterval = TimeSpan.FromHours(1);
            })
            .Build();

        // First call succeeds
        await policy.ExecuteAsync<string>(_ => Task.FromResult("ok"));

        // Second call rejected
        await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            policy.ExecuteAsync<string>(_ => Task.FromResult("ok")));
    }

    [Fact]
    public async Task TokenBucket_RejectedCallInvokesOnRejectedCallback()
    {
        bool callbackInvoked = false;
        var policy = ResiliencePolicy.Create()
            .RateLimiter(opts =>
            {
                opts.Algorithm = RateLimiterAlgorithm.TokenBucket;
                opts.BucketCapacity = 0; // empty from start
                opts.TokensPerInterval = 0;
                opts.ReplenishmentInterval = TimeSpan.FromHours(1);
                opts.OnRejected = _ => { callbackInvoked = true; return Task.CompletedTask; };
            })
            .Build();

        await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            policy.ExecuteAsync<int>(_ => Task.FromResult(1)));

        Assert.True(callbackInvoked);
    }

    [Fact]
    public async Task TokenBucket_ShorthandOverload_UsesDefaults()
    {
        var policy = ResiliencePolicy.Create()
            .RateLimiter(bucketCapacity: 10)
            .Build();

        // Should succeed for the first 10 calls
        for (int i = 0; i < 10; i++)
            await policy.ExecuteAsync<int>(_ => Task.FromResult(1));
    }

    // -----------------------------------------------------------------------
    // Sliding Window
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SlidingWindow_AllowsCallsUpToPermitLimit()
    {
        int limit = 3;
        var policy = ResiliencePolicy.Create()
            .RateLimiter(opts =>
            {
                opts.Algorithm = RateLimiterAlgorithm.SlidingWindow;
                opts.PermitLimit = limit;
                opts.Window = TimeSpan.FromMinutes(1); // long window so calls don't expire
            })
            .Build();

        for (int i = 0; i < limit; i++)
            await policy.ExecuteAsync<int>(_ => Task.FromResult(i));
    }

    [Fact]
    public async Task SlidingWindow_RejectsAbovePermitLimit()
    {
        var policy = ResiliencePolicy.Create()
            .RateLimiter(opts =>
            {
                opts.Algorithm = RateLimiterAlgorithm.SlidingWindow;
                opts.PermitLimit = 2;
                opts.Window = TimeSpan.FromMinutes(1);
            })
            .Build();

        await policy.ExecuteAsync<int>(_ => Task.FromResult(1));
        await policy.ExecuteAsync<int>(_ => Task.FromResult(2));

        await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            policy.ExecuteAsync<int>(_ => Task.FromResult(3)));
    }

    [Fact]
    public async Task SlidingWindow_AllowsCallsAfterWindowExpires()
    {
        var policy = ResiliencePolicy.Create()
            .RateLimiter(opts =>
            {
                opts.Algorithm = RateLimiterAlgorithm.SlidingWindow;
                opts.PermitLimit = 1;
                opts.Window = TimeSpan.FromMilliseconds(50); // very short window
            })
            .Build();

        await policy.ExecuteAsync<int>(_ => Task.FromResult(1));

        // Wait for window to expire
        await Task.Delay(100);

        // Should succeed again
        var result = await policy.ExecuteAsync<int>(_ => Task.FromResult(2));
        Assert.Equal(2, result);
    }

    // -----------------------------------------------------------------------
    // RateLimiterState independently
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RateLimiterState_TokenBucket_PermitsUpToCapacity()
    {
        var options = new RateLimiterOptions
        {
            Algorithm = RateLimiterAlgorithm.TokenBucket,
            BucketCapacity = 3,
            TokensPerInterval = 0,
            ReplenishmentInterval = TimeSpan.FromHours(1)
        };
        using var state = new RateLimiterState(options);

        Assert.True(await state.TryAcquireAsync(CancellationToken.None));
        Assert.True(await state.TryAcquireAsync(CancellationToken.None));
        Assert.True(await state.TryAcquireAsync(CancellationToken.None));
        Assert.False(await state.TryAcquireAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RateLimiterExceptionContainsAlgorithmName()
    {
        var policy = ResiliencePolicy.Create()
            .RateLimiter(opts =>
            {
                opts.Algorithm = RateLimiterAlgorithm.SlidingWindow;
                opts.PermitLimit = 0;
                opts.Window = TimeSpan.FromMinutes(1);
            })
            .Build();

        var ex = await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            policy.ExecuteAsync<int>(_ => Task.FromResult(1)));

        Assert.Equal("SlidingWindow", ex.Algorithm);
    }
}
