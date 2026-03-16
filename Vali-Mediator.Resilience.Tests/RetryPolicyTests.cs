using Vali_Mediator.Core.Result;
using Xunit;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Tests;

public class RetryPolicyTests
{
    // -----------------------------------------------------------------------
    // Basic retry
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Retry_SucceedsOnFirstAttempt_NeverRetries()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create().Retry(3).Build();

        var result = await policy.ExecuteAsync<string>(_ =>
        {
            calls++;
            return Task.FromResult("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Retry_FailsThenSucceeds_RetriesCorrectly()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create().Retry(3).Build();

        var result = await policy.ExecuteAsync<int>(_ =>
        {
            calls++;
            if (calls < 3) throw new InvalidOperationException("fail");
            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Retry_ExceedsMaxRetries_Throws()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create().Retry(2).Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new InvalidOperationException("always fails");
            }));

        Assert.Equal(3, calls); // 1 initial + 2 retries
    }

    [Fact]
    public async Task Retry_OnRetryCallback_InvokedForEachRetry()
    {
        var retryAttempts = new List<int>();
        var policy = ResiliencePolicy.Create()
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
                options.OnRetry = (ctx, _) =>
                {
                    retryAttempts.Add(ctx.AttemptNumber);
                    return Task.CompletedTask;
                };
            })
            .Build();

        await Assert.ThrowsAsync<Exception>(() =>
            policy.ExecuteAsync<int>(_ => throw new Exception("fail")));

        Assert.Equal(3, retryAttempts.Count);
    }

    [Fact]
    public async Task Retry_RetryOnSpecificException_DoesNotRetryOtherExceptions()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Retry(options =>
            {
                options.MaxRetries = 5;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
                options.RetryOnExceptions.Add(typeof(HttpRequestException));
            })
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new InvalidOperationException("not retried");
            }));

        Assert.Equal(1, calls); // no retry — wrong exception type
    }

    [Fact]
    public async Task Retry_RetryOnSpecificException_RetriesMatchingException()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
                options.RetryOnExceptions.Add(typeof(HttpRequestException));
            })
            .Build();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new HttpRequestException("http error");
            }));

        Assert.Equal(4, calls); // 1 + 3 retries
    }

    [Fact]
    public async Task Retry_RetryOnPredicate_RetriesWhenPredicateTrue()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
                options.RetryOnPredicate = ex => ex.Message == "transient";
            })
            .Build();

        await Assert.ThrowsAsync<Exception>(() =>
            policy.ExecuteAsync<int>(_ =>
            {
                calls++;
                throw new Exception("transient");
            }));

        Assert.Equal(3, calls);
    }

    // -----------------------------------------------------------------------
    // Backoff types
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(BackoffType.Fixed)]
    [InlineData(BackoffType.Linear)]
    [InlineData(BackoffType.Exponential)]
    [InlineData(BackoffType.ExponentialWithJitter)]
    public async Task Retry_AllBackoffTypes_CompleteWithoutError(BackoffType backoffType)
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.BackoffType = backoffType;
                options.InitialDelay = TimeSpan.FromMilliseconds(1);
                options.MaxDelay = TimeSpan.FromMilliseconds(10);
            })
            .Build();

        var result = await policy.ExecuteAsync<int>(_ =>
        {
            calls++;
            if (calls < 3) throw new Exception("retry me");
            return Task.FromResult(1);
        });

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Retry_CustomBackoff_UsesFactory()
    {
        var delays = new List<TimeSpan>();
        var policy = ResiliencePolicy.Create()
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.BackoffType = BackoffType.Custom;
                options.InitialDelay = TimeSpan.Zero;
                options.CustomDelayFactory = attempt => TimeSpan.FromMilliseconds(attempt * 10);
                options.OnRetry = (ctx, delay) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                };
            })
            .Build();

        await Assert.ThrowsAsync<Exception>(() =>
            policy.ExecuteAsync<int>(_ => throw new Exception("fail")));

        Assert.Equal(2, delays.Count);
        Assert.True(delays[1] > delays[0]); // delay grows with attempt index
    }

    // -----------------------------------------------------------------------
    // IResult-aware retry
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Retry_RetryOnErrorType_RetriesFailedResult()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
                options.RetryOnErrorTypes.Add(ErrorType.Failure);
            })
            .Build();

        // Result<T>.IsFailure with matching ErrorType should trigger retry
        // But since IResult retry only applies inside the CB/pipeline when the delegate
        // does NOT throw — we test via the pipeline's result predicate path
        var result = await policy.ExecuteAsync<Result<int>>(_ =>
        {
            calls++;
            if (calls < 3)
                return Task.FromResult(Result<int>.Fail("transient", ErrorType.Failure));
            return Task.FromResult(Result<int>.Ok(99));
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Value);
        Assert.Equal(3, calls);
    }

    // -----------------------------------------------------------------------
    // Void operation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Retry_VoidOperation_RetriesAndSucceeds()
    {
        int calls = 0;
        var policy = ResiliencePolicy.Create()
            .Retry(options =>
            {
                options.MaxRetries = 2;
                options.BackoffType = BackoffType.Fixed;
                options.InitialDelay = TimeSpan.Zero;
            })
            .Build();

        await policy.ExecuteAsync(_ =>
        {
            calls++;
            if (calls < 3) throw new Exception("fail");
            return Task.CompletedTask;
        });

        Assert.Equal(3, calls);
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Retry_CancelledToken_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var policy = ResiliencePolicy.Create().Retry(5).Build();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync(ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(0);
            }, cancellationToken: cts.Token));
    }
}
