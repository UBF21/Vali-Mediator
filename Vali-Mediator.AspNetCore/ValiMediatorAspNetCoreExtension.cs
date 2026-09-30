using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.Result;
using HttpIResult = Microsoft.AspNetCore.Http.IResult;

namespace Vali_Mediator.AspNetCore;

/// <summary>
/// Registration and <see cref="HttpContext"/>-aware overloads for the Result-to-HTTP mapping.
/// </summary>
public static class ValiMediatorAspNetCoreExtension
{
    /// <summary>
    /// Registers the <see cref="ResultHttpOptions"/> used by the <c>ToActionResult(HttpContext)</c> and
    /// <c>ToHttpResult(HttpContext)</c> overloads. Bind it from configuration with <c>section.Bind(o)</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Delegate to configure <see cref="ResultHttpOptions"/>.</param>
    public static IServiceCollection AddResultHttpOptions(
        this IServiceCollection services,
        Action<ResultHttpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ResultHttpOptions();
        configure(options);
        services.AddSingleton(options);
        return services;
    }

    /// <summary>Maps a <see cref="Result{T}"/> using the <see cref="ResultHttpOptions"/> registered in DI.</summary>
    public static IActionResult ToActionResult<T>(this Result<T> result, HttpContext httpContext)
        => result.ToActionResult(Resolve(httpContext));

    /// <summary>Maps a <see cref="Result"/> using the <see cref="ResultHttpOptions"/> registered in DI.</summary>
    public static IActionResult ToActionResult(this Result result, HttpContext httpContext)
        => result.ToActionResult(Resolve(httpContext));

    /// <summary>Maps a <see cref="Result{T}"/> using the <see cref="ResultHttpOptions"/> registered in DI.</summary>
    public static HttpIResult ToHttpResult<T>(this Result<T> result, HttpContext httpContext)
        => result.ToHttpResult(Resolve(httpContext));

    /// <summary>Maps a <see cref="Result"/> using the <see cref="ResultHttpOptions"/> registered in DI.</summary>
    public static HttpIResult ToHttpResult(this Result result, HttpContext httpContext)
        => result.ToHttpResult(Resolve(httpContext));

    private static ResultHttpOptions Resolve(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.RequestServices?.GetService<ResultHttpOptions>() ?? ResultHttpOptions.Default;
    }
}
