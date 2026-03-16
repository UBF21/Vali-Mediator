using Vali_Mediator_Resilience.Core.Enums;
using Xunit;
using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Tests;

public class TimeoutPolicyTests
{
    // -----------------------------------------------------------------------
    // Optimistic timeout
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Timeout_Optimistic_CompletesBeforeDeadline_ReturnsResult()
    {
        var policy = ResiliencePolicy.Create()
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(5);
                options.Strategy = TimeoutStrategy.Optimistic;
            })
            .Build();

        var result = await policy.ExecuteAsync(_ => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Timeout_Optimistic_ExceedsDeadline_ThrowsTimeoutException()
    {
        var policy = ResiliencePolicy.Create()
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromMilliseconds(50);
                options.Strategy = TimeoutStrategy.Optimistic;
            })
            .Build();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            policy.ExecuteAsync(async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return 0;
            }));
    }

    // -----------------------------------------------------------------------
    // Pessimistic timeout
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Timeout_Pessimistic_CompletesBeforeDeadline_ReturnsResult()
    {
        var policy = ResiliencePolicy.Create()
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(5);
                options.Strategy = TimeoutStrategy.Pessimistic;
            })
            .Build();

        var result = await policy.ExecuteAsync(_ => Task.FromResult("hello"));

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task Timeout_Pessimistic_ExceedsDeadline_ThrowsTimeoutException()
    {
        var policy = ResiliencePolicy.Create()
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromMilliseconds(50);
                options.Strategy = TimeoutStrategy.Pessimistic;
            })
            .Build();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            policy.ExecuteAsync(async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return 0;
            }));
    }

    // -----------------------------------------------------------------------
    // Callback
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Timeout_OnTimeoutCallback_InvokedBeforeException()
    {
        bool callbackInvoked = false;

        var policy = ResiliencePolicy.Create()
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromMilliseconds(50);
                options.Strategy = TimeoutStrategy.Pessimistic;
                options.OnTimeout = _ =>
                {
                    callbackInvoked = true;
                    return Task.CompletedTask;
                };
            })
            .Build();

        try
        {
            await policy.ExecuteAsync(async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return 0;
            });
        }
        catch (TimeoutException) { }

        Assert.True(callbackInvoked);
    }

    // -----------------------------------------------------------------------
    // Shorthand
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Timeout_Shorthand_WorksWithTimeSpan()
    {
        var policy = ResiliencePolicy.Create()
            .Timeout(TimeSpan.FromSeconds(10))
            .Build();

        var result = await policy.ExecuteAsync(_ => Task.FromResult(1));

        Assert.Equal(1, result);
    }

    // -----------------------------------------------------------------------
    // Void operation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Timeout_VoidOperation_ExceedsDeadline_Throws()
    {
        var policy = ResiliencePolicy.Create()
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromMilliseconds(50);
                options.Strategy = TimeoutStrategy.Pessimistic;
            })
            .Build();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            policy.ExecuteAsync(async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }));
    }
}
