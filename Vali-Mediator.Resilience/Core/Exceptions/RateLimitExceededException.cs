namespace Vali_Mediator_Resilience.Core.Exceptions;

/// <summary>
/// Thrown when a request is rejected because the rate limiter has exhausted
/// its available permits/tokens.
/// </summary>
public sealed class RateLimitExceededException : Exception
{
    /// <summary>The algorithm that triggered the rejection.</summary>
    public string Algorithm { get; }

    /// <summary>Creates the exception for a limiter using <paramref name="algorithm"/>.</summary>
    /// <param name="algorithm">Name of the rate-limiting algorithm.</param>
    public RateLimitExceededException(string algorithm)
        : base($"Rate limit exceeded (algorithm: {algorithm}). The call was rejected.")
    {
        Algorithm = algorithm;
    }
}
