using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vali_Mediator.AspNetCore;
using Vali_Mediator.Core.Result;
using Xunit;

namespace Vali_Mediator.AspNetCore.Tests;

/// <summary>HttpContext overloads when the request has no service provider at all.</summary>
public class ResultHttpContextFallbackTests
{
    private static HttpContext ContextWithoutServices() => new DefaultHttpContext { RequestServices = null! };

    [Fact]
    public void ToActionResult_NoRequestServices_UsesDefaultsAndHidesFailureDetail()
    {
        var result = Result<string>.Fail("db password=hunter2", ErrorType.Failure)
            .ToActionResult(ContextWithoutServices());

        var problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
        Assert.DoesNotContain("hunter2", System.Text.Json.JsonSerializer.Serialize(problem.Value));
    }

    [Fact]
    public void ToActionResult_NonGeneric_NoRequestServices_UsesDefaults()
    {
        var result = Result.Fail("boom", ErrorType.NotFound).ToActionResult(ContextWithoutServices());

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public void ToHttpResult_NoRequestServices_UsesDefaultsAndHidesFailureDetail()
    {
        var result = Result<string>.Fail("db password=hunter2", ErrorType.Failure)
            .ToHttpResult(ContextWithoutServices());

        var problem = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
        Assert.DoesNotContain("hunter2", problem.ProblemDetails.Detail ?? string.Empty);
    }

    [Fact]
    public void ToHttpResult_ServiceProviderWithoutOptions_UsesDefaultsToo()
    {
        var http = new DefaultHttpContext { RequestServices = new EmptyServiceProvider() };

        var result = Result<string>.Fail("db password=hunter2", ErrorType.Failure).ToHttpResult(http);

        var problem = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result);
        Assert.DoesNotContain("hunter2", problem.ProblemDetails.Detail ?? string.Empty);
    }

    [Fact]
    public void ToHttpResult_NonGeneric_NoRequestServices_DoesNotThrow()
    {
        var result = Result.Ok().ToHttpResult(ContextWithoutServices());

        Assert.NotNull(result);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
