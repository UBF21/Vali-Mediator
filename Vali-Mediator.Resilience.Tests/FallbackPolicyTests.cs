using Vali_Mediator.Core.Result;
using Xunit;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Tests;

public class FallbackPolicyTests
{
    // -----------------------------------------------------------------------
    // Static fallback value
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Fallback_StaticValue_ReturnedOnException()
    {
        var policy = ResiliencePolicy.Create()
            .Fallback<int>(options =>
            {
                options.FallbackValue = -1;
            });

        var result = await policy.ExecuteAsync(_ => throw new Exception("fail"));

        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task Fallback_StaticValue_NotUsedOnSuccess()
    {
        var policy = ResiliencePolicy.Create()
            .Fallback<int>(options =>
            {
                options.FallbackValue = -1;
            });

        var result = await policy.ExecuteAsync(_ => Task.FromResult(99));

        Assert.Equal(99, result);
    }

    // -----------------------------------------------------------------------
    // Factory fallback
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Fallback_Factory_InvokedWithContext()
    {
        string? capturedKey = null;

        var policy = ResiliencePolicy.Create("my-op")
            .Fallback<string>(options =>
            {
                options.FallbackFactory = ctx =>
                {
                    capturedKey = ctx.OperationKey;
                    return Task.FromResult("fallback");
                };
            });

        var result = await policy.ExecuteAsync(_ => throw new Exception("fail"));

        Assert.Equal("fallback", result);
        Assert.Equal("my-op", capturedKey);
    }

    // -----------------------------------------------------------------------
    // Conditional fallback
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Fallback_WithExceptionPredicate_ActivatesWhenPredicateTrue()
    {
        var policy = ResiliencePolicy.Create()
            .Fallback<int>(options =>
            {
                options.FallbackValue = 0;
                options.FallbackOnException = ex => ex is InvalidOperationException;
            });

        var result = await policy.ExecuteAsync(_ =>
            throw new InvalidOperationException("caught"));

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Fallback_WithExceptionPredicate_DoesNotActivateForOtherExceptions()
    {
        var policy = ResiliencePolicy.Create()
            .Fallback<int>(options =>
            {
                options.FallbackValue = 0;
                options.FallbackOnException = ex => ex is InvalidOperationException;
            });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            policy.ExecuteAsync(_ => throw new ArgumentException("not caught")));
    }

    // -----------------------------------------------------------------------
    // OnFallback callback
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Fallback_OnFallbackCallback_InvokedWithException()
    {
        Exception? capturedEx = null;

        var policy = ResiliencePolicy.Create()
            .Fallback<string>(options =>
            {
                options.FallbackValue = "default";
                options.OnFallback = (_, ex) =>
                {
                    capturedEx = ex;
                    return Task.CompletedTask;
                };
            });

        await policy.ExecuteAsync(_ => throw new InvalidOperationException("boom"));

        Assert.NotNull(capturedEx);
        Assert.IsType<InvalidOperationException>(capturedEx);
    }

    // -----------------------------------------------------------------------
    // Fallback with Result<T>
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Fallback_WithResultT_ReturnsFallbackResult()
    {
        var fallbackResult = Result<string>.Fail("Service unavailable.", ErrorType.Failure);

        var policy = ResiliencePolicy.Create()
            .Fallback<Result<string>>(options =>
            {
                options.FallbackValue = fallbackResult;
            });

        var result = await policy.ExecuteAsync(_ =>
            throw new Exception("downstream error"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Failure, result.ErrorType);
        Assert.Equal("Service unavailable.", result.Error);
    }

    // -----------------------------------------------------------------------
    // Fallback via non-generic policy
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Fallback_ViaResiliencePolicy_WithFallbackOptions()
    {
        var fallback = new Vali_Mediator_Resilience.Core.Options.FallbackOptions<int>
        {
            FallbackValue = 42
        };

        var policy = ResiliencePolicy.Create().Build();

        var result = await policy.ExecuteAsync<int>(
            _ => throw new Exception("fail"),
            fallback);

        Assert.Equal(42, result);
    }
}
