using Vali_Mediator.Core.Result;

namespace Vali_Mediator.AspNetCore;

/// <summary>
/// Controls how <see cref="ResultExtensions"/> renders error details in HTTP responses.
/// </summary>
public sealed class ResultHttpOptions
{
    internal const string GenericFailureDetail = "An unexpected error occurred.";

    /// <summary>
    /// When <c>false</c> (default), <see cref="ErrorType.Failure"/> (HTTP 500) responses carry a generic
    /// <c>detail</c> instead of <c>Result.Error</c>, so internal messages (for example exception text) are not leaked.
    /// Client errors (4xx) always include the error message.
    /// </summary>
    public bool ExposeErrorDetails { get; set; }

    /// <summary>Default options: 500 details are hidden.</summary>
    public static ResultHttpOptions Default => new();
}
