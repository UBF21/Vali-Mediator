using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.AspNetCore;
using Vali_Mediator.Core.Result;
using Xunit;
using HttpIResult = Microsoft.AspNetCore.Http.IResult;

namespace Vali_Mediator.AspNetCore.Tests;

public class ResultExtensionsTests
{
    private sealed record Rendered(int Status, string? ContentType, JsonElement? Body);

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        services.AddControllers();
        return services.BuildServiceProvider();
    }

    private static async Task<Rendered> RenderHttp(HttpIResult result)
    {
        using var sp = BuildServices();
        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Response.Body = new MemoryStream();
        await result.ExecuteAsync(ctx);
        return await Read(ctx);
    }

    private static async Task<Rendered> RenderMvc(IActionResult result)
    {
        using var sp = BuildServices();
        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Request.Headers.Accept = "application/json";
        ctx.Response.Body = new MemoryStream();
        var actionContext = new ActionContext(ctx, new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        await result.ExecuteResultAsync(actionContext);
        return await Read(ctx);
    }

    private static async Task<Rendered> Read(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        var text = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        JsonElement? body = string.IsNullOrEmpty(text) ? null : JsonDocument.Parse(text).RootElement.Clone();
        return new Rendered(ctx.Response.StatusCode, ctx.Response.ContentType, body);
    }

    private static string? Str(Rendered r, string name)
        => r.Body is { } b && b.TryGetProperty(name, out var p) ? p.GetString() : null;

    private static Result<string> Fail(ErrorType type) => Result<string>.Fail("boom secret", type);

    public static IEnumerable<object[]> ClientErrors() => new[]
    {
        new object[] { ErrorType.NotFound, 404, "Not Found" },
        new object[] { ErrorType.Conflict, 409, "Conflict" },
        new object[] { ErrorType.Unauthorized, 401, "Unauthorized" },
        new object[] { ErrorType.Forbidden, 403, "Forbidden" },
        new object[] { ErrorType.Validation, 400, "Validation Failed" },
    };

    [Fact]
    public async Task GenericSuccess_Returns200WithValue_InBothStyles()
    {
        var ok = Result<string>.Ok("hello");

        var mvc = await RenderMvc(ok.ToActionResult());
        var http = await RenderHttp(ok.ToHttpResult());

        Assert.Equal(200, mvc.Status);
        Assert.Equal(200, http.Status);
        Assert.Equal("hello", mvc.Body!.Value.GetString());
        Assert.Equal("hello", http.Body!.Value.GetString());
    }

    [Fact]
    public async Task NonGenericSuccess_Returns204_InBothStyles()
    {
        var mvc = await RenderMvc(Result.Ok().ToActionResult());
        var http = await RenderHttp(Result.Ok().ToHttpResult());

        Assert.Equal(204, mvc.Status);
        Assert.Equal(204, http.Status);
        Assert.Null(mvc.Body);
        Assert.Null(http.Body);
    }

    [Theory]
    [MemberData(nameof(ClientErrors))]
    public async Task GenericClientErrors_ShowMessage_AndMatchAcrossStyles(ErrorType type, int status, string title)
    {
        var mvc = await RenderMvc(Fail(type).ToActionResult());
        var http = await RenderHttp(Fail(type).ToHttpResult());

        foreach (var r in new[] { mvc, http })
        {
            Assert.Equal(status, r.Status);
            Assert.Equal(title, Str(r, "title"));
            Assert.Equal("boom secret", Str(r, "detail"));
            Assert.Equal(status, r.Body!.Value.GetProperty("status").GetInt32());
        }
    }

    [Theory]
    [MemberData(nameof(ClientErrors))]
    public async Task NonGenericClientErrors_ShowMessage_AndMatchAcrossStyles(ErrorType type, int status, string title)
    {
        var mvc = await RenderMvc(Result.Fail("boom secret", type).ToActionResult());
        var http = await RenderHttp(Result.Fail("boom secret", type).ToHttpResult());

        foreach (var r in new[] { mvc, http })
        {
            Assert.Equal(status, r.Status);
            Assert.Equal(title, Str(r, "title"));
            Assert.Equal("boom secret", Str(r, "detail"));
        }
    }

    [Fact]
    public async Task Failure_HidesErrorMessage_ByDefault()
    {
        var results = new[]
        {
            await RenderMvc(Fail(ErrorType.Failure).ToActionResult()),
            await RenderHttp(Fail(ErrorType.Failure).ToHttpResult()),
            await RenderMvc(Result.Fail("boom secret", ErrorType.Failure).ToActionResult()),
            await RenderHttp(Result.Fail("boom secret", ErrorType.Failure).ToHttpResult()),
        };

        foreach (var r in results)
        {
            Assert.Equal(500, r.Status);
            Assert.Equal("Internal Server Error", Str(r, "title"));
            Assert.Equal("An unexpected error occurred.", Str(r, "detail"));
            Assert.DoesNotContain("secret", r.Body!.Value.GetRawText());
        }
    }

    [Fact]
    public async Task Failure_ExposesErrorMessage_WhenOptedIn()
    {
        var options = new ResultHttpOptions { ExposeErrorDetails = true };

        var results = new[]
        {
            await RenderMvc(Fail(ErrorType.Failure).ToActionResult(options)),
            await RenderHttp(Fail(ErrorType.Failure).ToHttpResult(options)),
            await RenderMvc(Result.Fail("boom secret", ErrorType.Failure).ToActionResult(options)),
            await RenderHttp(Result.Fail("boom secret", ErrorType.Failure).ToHttpResult(options)),
        };

        foreach (var r in results)
        {
            Assert.Equal(500, r.Status);
            Assert.Equal("boom secret", Str(r, "detail"));
        }
    }

    [Fact]
    public async Task UninitializedDefaultResult_IsA500WithoutLeaking()
    {
        var mvc = await RenderMvc(default(Result<string>).ToActionResult());
        var http = await RenderHttp(default(Result).ToHttpResult());

        Assert.Equal(500, mvc.Status);
        Assert.Equal(500, http.Status);
        Assert.Equal("An unexpected error occurred.", Str(mvc, "detail"));
    }

    [Fact]
    public async Task ValidationWithErrors_ReturnsValidationProblem_InBothStyles()
    {
        var errors = new Dictionary<string, List<string>>
        {
            ["Name"] = new List<string> { "required", "too short" },
            ["Age"] = new List<string> { "must be positive" }
        };
        var result = Result<string>.Fail(errors, ErrorType.Validation);

        var mvc = await RenderMvc(result.ToActionResult());
        var http = await RenderHttp(result.ToHttpResult());

        foreach (var r in new[] { mvc, http })
        {
            Assert.Equal(400, r.Status);
            var errs = r.Body!.Value.GetProperty("errors");
            Assert.Equal(2, errs.GetProperty("Name").GetArrayLength());
            Assert.Equal("must be positive", errs.GetProperty("Age")[0].GetString());
        }

        Assert.Equal(Str(mvc, "title"), Str(http, "title"));
    }

    [Fact]
    public void MvcValidationWithErrors_UsesValidationProblemDetails()
    {
        var errors = new Dictionary<string, List<string>> { ["Name"] = new List<string> { "required" } };

        var action = Result<string>.Fail(errors, ErrorType.Validation).ToActionResult();

        var bad = Assert.IsType<BadRequestObjectResult>(action);
        Assert.IsType<ValidationProblemDetails>(bad.Value);
    }

    [Fact]
    public void MvcResultTypes_MatchStatusSemantics()
    {
        Assert.IsType<OkObjectResult>(Result<string>.Ok("x").ToActionResult());
        Assert.IsType<NoContentResult>(Result.Ok().ToActionResult());
        Assert.IsType<NotFoundObjectResult>(Fail(ErrorType.NotFound).ToActionResult());
        Assert.IsType<ConflictObjectResult>(Fail(ErrorType.Conflict).ToActionResult());
        Assert.IsType<UnauthorizedObjectResult>(Fail(ErrorType.Unauthorized).ToActionResult());
        Assert.Equal(403, Assert.IsType<ObjectResult>(Fail(ErrorType.Forbidden).ToActionResult()).StatusCode);
        Assert.Equal(500, Assert.IsType<ObjectResult>(Fail(ErrorType.Failure).ToActionResult()).StatusCode);
    }

    [Fact]
    public async Task ProblemResponses_UseProblemJsonContentType_InMinimalApi()
    {
        var http = await RenderHttp(Fail(ErrorType.NotFound).ToHttpResult());

        Assert.StartsWith("application/problem+json", http.ContentType);
    }

    [Fact]
    public void NullOptions_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Fail(ErrorType.Failure).ToActionResult((ResultHttpOptions)null!));
        Assert.Throws<ArgumentNullException>(() => Fail(ErrorType.Failure).ToHttpResult((ResultHttpOptions)null!));
        Assert.Throws<ArgumentNullException>(() => Result.Ok().ToActionResult((ResultHttpOptions)null!));
        Assert.Throws<ArgumentNullException>(() => Result.Ok().ToHttpResult((ResultHttpOptions)null!));
    }

    [Fact]
    public void DefaultOptions_HideDetails()
    {
        Assert.False(ResultHttpOptions.Default.ExposeErrorDetails);
        Assert.False(new ResultHttpOptions().ExposeErrorDetails);
    }
}
