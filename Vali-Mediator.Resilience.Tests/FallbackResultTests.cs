using Vali_Mediator.Core.Result;
using Vali_Mediator_Resilience.Core.Policies;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class FallbackResultTests
{
    private static ResiliencePolicy<Result<int>> Policy(Action<int>? onFallback = null)
        => ResiliencePolicy.Create().Fallback<Result<int>>(o =>
        {
            o.FallbackValue = Result<int>.Ok(-1);
            o.FallbackOnResultPredicate = r => r.IsFailure;
            o.OnFallback = (_, ex) => { onFallback?.Invoke(ex == null ? 0 : 1); return Task.CompletedTask; };
        });

    [Fact]
    public async Task FailedResult_ActivatesTheFallback_WithoutAnException()
    {
        int seenException = -1;
        var policy = Policy(x => seenException = x);

        var result = await policy.ExecuteAsync(_ => Task.FromResult(Result<int>.Fail("nope", ErrorType.Failure)));

        Assert.Equal(-1, result.Value);
        Assert.Equal(0, seenException); // OnFallback got a null exception
    }

    [Fact]
    public async Task SuccessfulResult_IsReturnedUntouched()
    {
        var result = await Policy().ExecuteAsync(_ => Task.FromResult(Result<int>.Ok(7)));

        Assert.Equal(7, result.Value);
    }

    [Fact]
    public async Task WithoutTheResultPredicate_FailedResultsPassThrough()
    {
        var policy = ResiliencePolicy.Create().Fallback<Result<int>>(o => o.FallbackValue = Result<int>.Ok(-1));

        var result = await policy.ExecuteAsync(_ => Task.FromResult(Result<int>.Fail("nope", ErrorType.Failure)));

        Assert.True(result.IsFailure);
    }
}
