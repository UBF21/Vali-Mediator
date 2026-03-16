using Vali_Mediator.Core.Result;
using Xunit;

namespace Vali_Mediator.Tests;

public class ResultTests
{
    // -------------------------------------------------------------------------
    // Result<T> basic
    // -------------------------------------------------------------------------

    [Fact]
    public void Ok_SetsIsSuccessTrue()
    {
        var result = Result<int>.Ok(42);
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
        Assert.Equal(ErrorType.None, result.ErrorType);
    }

    [Fact]
    public void Fail_SetsIsFailureTrue()
    {
        var result = Result<int>.Fail("something went wrong", ErrorType.NotFound);
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("something went wrong", result.Error);
        Assert.Equal(ErrorType.NotFound, result.ErrorType);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void Fail_WithValidationErrors_PopulatesValidationErrors()
    {
        var errors = new Dictionary<string, List<string>>
        {
            { "Name", new List<string> { "Required", "Too short" } },
            { "Email", new List<string> { "Invalid format" } }
        };

        var result = Result<int>.Fail(errors);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.ErrorType);
        Assert.NotNull(result.ValidationErrors);
        Assert.Equal(2, result.ValidationErrors!.Count);
        Assert.Contains("Required", result.ValidationErrors["Name"]);
        Assert.Contains("Invalid format", result.ValidationErrors["Email"]);
    }

    // -------------------------------------------------------------------------
    // Map
    // -------------------------------------------------------------------------

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var result = Result<int>.Ok(5).Map(x => x * 2);
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public void Map_OnFailure_PropagatesFailure()
    {
        var result = Result<int>.Fail("error", ErrorType.NotFound).Map(x => x * 2);
        Assert.True(result.IsFailure);
        Assert.Equal("error", result.Error);
        Assert.Equal(ErrorType.NotFound, result.ErrorType);
    }

    // -------------------------------------------------------------------------
    // Bind
    // -------------------------------------------------------------------------

    [Fact]
    public void Bind_OnSuccess_ChainsOperation()
    {
        var result = Result<int>.Ok(5).Bind(x => Result<string>.Ok($"value={x}"));
        Assert.True(result.IsSuccess);
        Assert.Equal("value=5", result.Value);
    }

    [Fact]
    public void Bind_OnFailure_PropagatesFailure()
    {
        var result = Result<int>.Fail("err", ErrorType.Conflict)
            .Bind(x => Result<string>.Ok(x.ToString()));
        Assert.True(result.IsFailure);
        Assert.Equal("err", result.Error);
        Assert.Equal(ErrorType.Conflict, result.ErrorType);
    }

    // -------------------------------------------------------------------------
    // MapAsync / BindAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MapAsync_OnSuccess_TransformsValue()
    {
        var result = await Result<int>.Ok(3)
            .MapAsync(x => Task.FromResult(x * 10));
        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Value);
    }

    [Fact]
    public async Task BindAsync_OnSuccess_ChainsOperation()
    {
        var result = await Result<int>.Ok(7)
            .BindAsync(x => Task.FromResult(Result<string>.Ok($"id:{x}")));
        Assert.True(result.IsSuccess);
        Assert.Equal("id:7", result.Value);
    }

    // -------------------------------------------------------------------------
    // Tap
    // -------------------------------------------------------------------------

    [Fact]
    public void Tap_OnSuccess_ExecutesSideEffect()
    {
        var captured = 0;
        var result = Result<int>.Ok(99).Tap(v => captured = v);
        Assert.Equal(99, captured);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Tap_OnFailure_DoesNotExecute()
    {
        var called = false;
        Result<int>.Fail("err").Tap(_ => called = true);
        Assert.False(called);
    }

    // -------------------------------------------------------------------------
    // OnFailure
    // -------------------------------------------------------------------------

    [Fact]
    public void OnFailure_OnFailure_ExecutesSideEffect()
    {
        string? capturedError = null;
        ErrorType capturedType = ErrorType.None;

        Result<int>.Fail("bad input", ErrorType.Validation)
            .OnFailure((err, type) => { capturedError = err; capturedType = type; });

        Assert.Equal("bad input", capturedError);
        Assert.Equal(ErrorType.Validation, capturedType);
    }

    [Fact]
    public void OnFailure_OnSuccess_DoesNotExecute()
    {
        var called = false;
        Result<int>.Ok(1).OnFailure((_, _) => called = true);
        Assert.False(called);
    }

    // -------------------------------------------------------------------------
    // Match
    // -------------------------------------------------------------------------

    [Fact]
    public void Match_OnSuccess_CallsOnSuccess()
    {
        var result = Result<int>.Ok(21).Match(v => v * 2, (_, _) => -1);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Match_OnFailure_CallsOnFailure()
    {
        var result = Result<int>.Fail("oops", ErrorType.Failure).Match(_ => 0, (err, _) => err.Length);
        Assert.Equal("oops".Length, result);
    }

    // -------------------------------------------------------------------------
    // Implicit conversion
    // -------------------------------------------------------------------------

    [Fact]
    public void ImplicitConversion_CreatesOkResult()
    {
        Result<string> result = "hello";
        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    // -------------------------------------------------------------------------
    // Result (non-generic / void)
    // -------------------------------------------------------------------------

    [Fact]
    public void ResultVoid_Ok_IsSuccess()
    {
        var result = Result.Ok();
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
        Assert.Equal(ErrorType.None, result.ErrorType);
    }

    [Fact]
    public void ResultVoid_Fail_IsFailure()
    {
        var result = Result.Fail("not found", ErrorType.NotFound);
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("not found", result.Error);
        Assert.Equal(ErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public void ResultVoid_Map_TransformsToTypedResult()
    {
        var result = Result.Ok().Map(() => 42);
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ResultVoid_Map_OnFailure_PropagatesError()
    {
        var result = Result.Fail("oops", ErrorType.Failure).Map(() => 42);
        Assert.True(result.IsFailure);
        Assert.Equal("oops", result.Error);
    }

    [Fact]
    public void ResultVoid_Match_CallsCorrectBranch()
    {
        var successMsg = Result.Ok().Match(() => "ok", (_, _) => "fail");
        Assert.Equal("ok", successMsg);

        var failMsg = Result.Fail("err", ErrorType.Failure).Match(() => "ok", (e, _) => e);
        Assert.Equal("err", failMsg);
    }

    [Fact]
    public void ResultVoid_Tap_ExecutesOnSuccess()
    {
        var called = false;
        Result.Ok().Tap(() => called = true);
        Assert.True(called);
    }

    [Fact]
    public void ResultVoid_Tap_DoesNotExecuteOnFailure()
    {
        var called = false;
        Result.Fail("err").Tap(() => called = true);
        Assert.False(called);
    }
}
