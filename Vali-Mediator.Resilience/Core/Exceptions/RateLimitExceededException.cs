namespace Vali_Mediator_Resilience.Core.Exceptions;

/// <summary>
/// Thrown when a request is rejected because the rate limiter has exhausted
/// its available permits/tokens.
/// </summary>
public sealed class RateLimitExceededException : Exception
{
    /// <summary>The algorithm that triggered the rejection.</summary>
    public string Algorithm { get; }

    public RateLimitExceededException(string algorithm)
        : base($"Rate limit exceeded (algorithm: {algorithm}). The call was rejected.")
    {
        Algorithm = algorithm;
    }
}
