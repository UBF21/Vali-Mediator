using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vali_Mediator.Core.Result;
using HttpIResult = Microsoft.AspNetCore.Http.IResult;

namespace Vali_Mediator.AspNetCore;

/// <summary>
/// Extension methods for mapping <see cref="Result{T}"/> and <see cref="Result"/> to ASP.NET Core HTTP responses.
/// MVC (<c>ToActionResult</c>) and Minimal API (<c>ToHttpResult</c>) produce the same status codes, titles and bodies.
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
    ///   <item><see cref="ErrorType.Failure"/> → 500 ProblemDetails with a generic detail (see <see cref="ResultHttpOptions"/>)</item>
    /// </list>
    /// </summary>
    /// <typeparam name="T">The result value type.</typeparam>
    /// <param name="result">The result to map.</param>
    public static IActionResult ToActionResult<T>(this Result<T> result)
        => result.ToActionResult(ResultHttpOptions.Default);

    /// <summary>
    /// Maps a <see cref="Result{T}"/> to an <see cref="IActionResult"/> using the given options.
    /// </summary>
    /// <typeparam name="T">The result value type.</typeparam>
    /// <param name="result">The result to map.</param>
    /// <param name="options">Rendering options; see <see cref="ResultHttpOptions"/>.</param>
    public static IActionResult ToActionResult<T>(this Result<T> result, ResultHttpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (result.IsSuccess)
            return new OkObjectResult(result.Value);

        if (result.ErrorType == ErrorType.Validation)
            return BuildValidationActionResult(result.ValidationErrors, result.Error);

        return ToFailureActionResult(result.ErrorType, result.Error, options);
    }

    // -----------------------------------------------------------------------
    // Result (non-generic) → IActionResult (MVC Controllers)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps a non-generic <see cref="Result"/> to an <see cref="IActionResult"/>.
    /// On success returns 204 No Content.
    /// </summary>
    /// <param name="result">The result to map.</param>
    public static IActionResult ToActionResult(this Result result)
        => result.ToActionResult(ResultHttpOptions.Default);

    /// <summary>
    /// Maps a non-generic <see cref="Result"/> to an <see cref="IActionResult"/> using the given options.
    /// On success returns 204 No Content.
    /// </summary>
    /// <param name="result">The result to map.</param>
    /// <param name="options">Rendering options; see <see cref="ResultHttpOptions"/>.</param>
    public static IActionResult ToActionResult(this Result result, ResultHttpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (result.IsSuccess)
            return new NoContentResult();

        return ToFailureActionResult(result.ErrorType, result.Error, options);
    }

    // -----------------------------------------------------------------------
    // Result<T> → IResult (Minimal API)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps a <see cref="Result{T}"/> to a Minimal API <see cref="HttpIResult"/>.
    /// Uses the same status codes, titles and bodies as <c>ToActionResult</c>.
    /// </summary>
    /// <typeparam name="T">The result value type.</typeparam>
    /// <param name="result">The result to map.</param>
    public static HttpIResult ToHttpResult<T>(this Result<T> result)
        => result.ToHttpResult(ResultHttpOptions.Default);

    /// <summary>
    /// Maps a <see cref="Result{T}"/> to a Minimal API <see cref="HttpIResult"/> using the given options.
    /// </summary>
    /// <typeparam name="T">The result value type.</typeparam>
    /// <param name="result">The result to map.</param>
    /// <param name="options">Rendering options; see <see cref="ResultHttpOptions"/>.</param>
    public static HttpIResult ToHttpResult<T>(this Result<T> result, ResultHttpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (result.IsSuccess)
            return Results.Ok(result.Value);

        if (result.ErrorType == ErrorType.Validation)
            return BuildValidationHttpResult(result.ValidationErrors, result.Error);

        return Results.Problem(BuildFailureProblem(result.ErrorType, result.Error, options));
    }

    // -----------------------------------------------------------------------
    // Result (non-generic) → IResult (Minimal API)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps a non-generic <see cref="Result"/> to a Minimal API <see cref="HttpIResult"/>.
    /// On success returns 204 No Content.
    /// </summary>
    /// <param name="result">The result to map.</param>
    public static HttpIResult ToHttpResult(this Result result)
        => result.ToHttpResult(ResultHttpOptions.Default);

    /// <summary>
    /// Maps a non-generic <see cref="Result"/> to a Minimal API <see cref="HttpIResult"/> using the given options.
    /// On success returns 204 No Content.
    /// </summary>
    /// <param name="result">The result to map.</param>
    /// <param name="options">Rendering options; see <see cref="ResultHttpOptions"/>.</param>
    public static HttpIResult ToHttpResult(this Result result, ResultHttpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (result.IsSuccess)
            return Results.NoContent();

        return Results.Problem(BuildFailureProblem(result.ErrorType, result.Error, options));
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

        return Results.Problem(BuildProblemDetails(400, "Validation Failed", error));
    }

    private static IActionResult ToFailureActionResult(ErrorType errorType, string? error, ResultHttpOptions options)
    {
        var problem = BuildFailureProblem(errorType, error, options);
        return problem.Status switch
        {
            400 => new BadRequestObjectResult(problem),
            404 => new NotFoundObjectResult(problem),
            409 => new ConflictObjectResult(problem),
            401 => new UnauthorizedObjectResult(problem),
            _ => new ObjectResult(problem) { StatusCode = problem.Status }
        };
    }

    // Single source of truth for status/title/detail so MVC and Minimal API responses match.
    private static ProblemDetails BuildFailureProblem(ErrorType errorType, string? error, ResultHttpOptions options)
    {
        switch (errorType)
        {
            case ErrorType.Validation: return BuildProblemDetails(400, "Validation Failed", error);
            case ErrorType.NotFound: return BuildProblemDetails(404, "Not Found", error);
            case ErrorType.Conflict: return BuildProblemDetails(409, "Conflict", error);
            case ErrorType.Unauthorized: return BuildProblemDetails(401, "Unauthorized", error);
            case ErrorType.Forbidden: return BuildProblemDetails(403, "Forbidden", error);
            default:
                var detail = options.ExposeErrorDetails ? error : ResultHttpOptions.GenericFailureDetail;
                return BuildProblemDetails(500, "Internal Server Error", detail);
        }
    }

    private static ProblemDetails BuildProblemDetails(int status, string title, string? detail)
        => new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        };
}
