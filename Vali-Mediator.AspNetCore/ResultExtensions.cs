using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vali_Mediator.Core.Result;
using HttpIResult = Microsoft.AspNetCore.Http.IResult;

namespace Vali_Mediator.AspNetCore;

/// <summary>
/// Extension methods for mapping <see cref="Result{T}"/> and <see cref="Result"/> to ASP.NET Core HTTP responses.
/// </summary>
public static class ResultExtensions
{
    // -----------------------------------------------------------------------
    // Result<T> → IActionResult (MVC Controllers)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps a <see cref="Result{T}"/> to an <see cref="IActionResult"/>.
    /// <list type="bullet">
    ///   <item><see cref="ErrorType.None"/> → 200 OK with value</item>
    ///   <item><see cref="ErrorType.Validation"/> → 400 with structured errors (if ValidationErrors populated) or ProblemDetails</item>
    ///   <item><see cref="ErrorType.NotFound"/> → 404 ProblemDetails</item>
    ///   <item><see cref="ErrorType.Conflict"/> → 409 ProblemDetails</item>
    ///   <item><see cref="ErrorType.Unauthorized"/> → 401 ProblemDetails</item>
    ///   <item><see cref="ErrorType.Forbidden"/> → 403 ProblemDetails</item>
    ///   <item><see cref="ErrorType.Failure"/> → 500 ProblemDetails</item>
    /// </list>
    /// </summary>
    public static IActionResult ToActionResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return new OkObjectResult(result.Value);

        return result.ErrorType switch
        {
            ErrorType.Validation => BuildValidationActionResult(result.ValidationErrors, result.Error),
            ErrorType.NotFound => new NotFoundObjectResult(BuildProblemDetails(404, "Not Found", result.Error)),
            ErrorType.Conflict => new ConflictObjectResult(BuildProblemDetails(409, "Conflict", result.Error)),
            ErrorType.Unauthorized => new UnauthorizedObjectResult(BuildProblemDetails(401, "Unauthorized", result.Error)),
            ErrorType.Forbidden => new ObjectResult(BuildProblemDetails(403, "Forbidden", result.Error)) { StatusCode = 403 },
            _ => new ObjectResult(BuildProblemDetails(500, "Internal Server Error", result.Error)) { StatusCode = 500 }
        };
    }

    // -----------------------------------------------------------------------
    // Result (non-generic) → IActionResult (MVC Controllers)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps a non-generic <see cref="Result"/> to an <see cref="IActionResult"/>.
    /// On success returns 204 No Content.
    /// </summary>
    public static IActionResult ToActionResult(this Result result)
    {
        if (result.IsSuccess)
            return new NoContentResult();

        return result.ErrorType switch
        {
            ErrorType.Validation => new BadRequestObjectResult(BuildProblemDetails(400, "Validation Failed", result.Error)),
            ErrorType.NotFound => new NotFoundObjectResult(BuildProblemDetails(404, "Not Found", result.Error)),
            ErrorType.Conflict => new ConflictObjectResult(BuildProblemDetails(409, "Conflict", result.Error)),
            ErrorType.Unauthorized => new UnauthorizedObjectResult(BuildProblemDetails(401, "Unauthorized", result.Error)),
            ErrorType.Forbidden => new ObjectResult(BuildProblemDetails(403, "Forbidden", result.Error)) { StatusCode = 403 },
            _ => new ObjectResult(BuildProblemDetails(500, "Internal Server Error", result.Error)) { StatusCode = 500 }
        };
    }

    // -----------------------------------------------------------------------
    // Result<T> → IResult (Minimal API)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps a <see cref="Result{T}"/> to a Minimal API <see cref="IResult"/>.
    /// </summary>
    public static HttpIResult ToHttpResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return Results.Ok(result.Value);

        return result.ErrorType switch
        {
            ErrorType.Validation => BuildValidationHttpResult(result.ValidationErrors, result.Error),
            ErrorType.NotFound => Results.NotFound(BuildProblemDetails(404, "Not Found", result.Error)),
            ErrorType.Conflict => Results.Conflict(BuildProblemDetails(409, "Conflict", result.Error)),
            ErrorType.Unauthorized => Results.Unauthorized(),
            ErrorType.Forbidden => Results.StatusCode(403),
            _ => Results.Problem(result.Error, statusCode: 500)
        };
    }

    // -----------------------------------------------------------------------
    // Result (non-generic) → IResult (Minimal API)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps a non-generic <see cref="Result"/> to a Minimal API <see cref="IResult"/>.
    /// On success returns 204 No Content.
    /// </summary>
    public static HttpIResult ToHttpResult(this Result result)
    {
        if (result.IsSuccess)
            return Results.NoContent();

        return result.ErrorType switch
        {
            ErrorType.Validation => Results.Problem(result.Error, statusCode: 400),
            ErrorType.NotFound => Results.NotFound(BuildProblemDetails(404, "Not Found", result.Error)),
            ErrorType.Conflict => Results.Conflict(BuildProblemDetails(409, "Conflict", result.Error)),
            ErrorType.Unauthorized => Results.Unauthorized(),
            ErrorType.Forbidden => Results.StatusCode(403),
            _ => Results.Problem(result.Error, statusCode: 500)
        };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IActionResult BuildValidationActionResult(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? validationErrors,
        string? error)
    {
        if (validationErrors != null && validationErrors.Count > 0)
        {
            var problemDetails = new ValidationProblemDetails(
                validationErrors.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToArray()));
            return new BadRequestObjectResult(problemDetails);
        }

        return new BadRequestObjectResult(BuildProblemDetails(400, "Validation Failed", error));
    }

    private static HttpIResult BuildValidationHttpResult(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? validationErrors,
        string? error)
    {
        if (validationErrors != null && validationErrors.Count > 0)
        {
            var errors = validationErrors.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToArray());
            return Results.ValidationProblem(errors);
        }

        return Results.Problem(error, statusCode: 400);
    }

    private static ProblemDetails BuildProblemDetails(int status, string title, string? detail)
        => new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        };
}
