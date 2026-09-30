using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.AspNetCore;
using Vali_Mediator.Core.Result;
using Xunit;
using HttpIResult = Microsoft.AspNetCore.Http.IResult;

namespace Vali_Mediator.AspNetCore.Tests;

/// <summary><c>ResultHttpOptions</c> registered in DI and honored by the HttpContext overloads.</summary>
public class ResultHttpConfigurationTests
{
    private const string Secret = "connection string leaked";

    private static ServiceProvider Build(Action<ResultHttpOptions>? configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        services.AddControllers();
        if (configure is not null) services.AddResultHttpOptions(configure);
        return services.BuildServiceProvider();
    }

    private static async Task<(int Status, string Detail)> RenderHttp(HttpIResult result, IServiceProvider sp)
    {
        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Response.Body = new MemoryStream();
        await result.ExecuteAsync(ctx);
        return Read(ctx);
    }

    private static async Task<(int Status, string Detail)> RenderMvc(IActionResult result, IServiceProvider sp)
    {
        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Request.Headers.Accept = "application/json";
        ctx.Response.Body = new MemoryStream();
        await result.ExecuteResultAsync(new ActionContext(ctx, new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        return Read(ctx);
    }

    private static (int Status, string Detail) Read(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        var text = new StreamReader(ctx.Response.Body).ReadToEnd();
        var detail = JsonDocument.Parse(text).RootElement.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
        return (ctx.Response.StatusCode, detail);
    }

    private static HttpContext ContextWith(IServiceProvider sp) => new DefaultHttpContext { RequestServices = sp };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExposeErrorDetails_ConfiguredThroughDi_IsHonoredByBothStyles(bool expose)
    {
        using var sp = Build(o => o.ExposeErrorDetails = expose);
        var failure = Result<string>.Fail(Secret, ErrorType.Failure);

        var http = await RenderHttp(failure.ToHttpResult(ContextWith(sp)), sp);
        var mvc = await RenderMvc(failure.ToActionResult(ContextWith(sp)), sp);

        Assert.Equal(500, http.Status);
        Assert.Equal(500, mvc.Status);
        Assert.Equal(expose, http.Detail.Contains(Secret));
        Assert.Equal(expose, mvc.Detail.Contains(Secret));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExposeErrorDetails_ConfiguredThroughDi_IsHonoredByTheNonGenericResult(bool expose)
    {
        using var sp = Build(o => o.ExposeErrorDetails = expose);
        var failure = Result.Fail(Secret, ErrorType.Failure);

        var http = await RenderHttp(failure.ToHttpResult(ContextWith(sp)), sp);
        var mvc = await RenderMvc(failure.ToActionResult(ContextWith(sp)), sp);

        Assert.Equal(expose, http.Detail.Contains(Secret));
        Assert.Equal(expose, mvc.Detail.Contains(Secret));
    }

    [Fact]
    public async Task WithoutRegistration_DetailsStayHidden()
    {
        using var sp = Build(null);

        var http = await RenderHttp(Result<string>.Fail(Secret, ErrorType.Failure).ToHttpResult(ContextWith(sp)), sp);

        Assert.DoesNotContain(Secret, http.Detail);
    }

    [Theory]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    public async Task ClientErrors_ShowTheirMessage_RegardlessOfTheOption(ErrorType type, int status)
    {
        using var hidden = Build(o => o.ExposeErrorDetails = false);

        var http = await RenderHttp(Result<string>.Fail(Secret, type).ToHttpResult(ContextWith(hidden)), hidden);

        Assert.Equal(status, http.Status);
        Assert.Contains(Secret, http.Detail);
    }

    [Fact]
    public void Default_IsAFreshInstance_SoItCannotBeMutatedGlobally()
    {
        ResultHttpOptions.Default.ExposeErrorDetails = true;

        Assert.False(ResultHttpOptions.Default.ExposeErrorDetails);
    }

    [Fact]
    public void HttpContextOverloads_RejectNullContext()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string>.Ok("x").ToHttpResult((HttpContext)null!));
        Assert.Throws<ArgumentNullException>(() => Result.Ok().ToActionResult((HttpContext)null!));
    }

    [Fact]
    public void AddResultHttpOptions_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddResultHttpOptions(null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddResultHttpOptions(_ => { }));
    }
}
