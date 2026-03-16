namespace Vali_Mediator_Resilience.Core.Enums;

/// <summary>Rate limiting algorithm used by the rate-limiter policy.</summary>
public enum RateLimiterAlgorithm
{
    /// <summary>
    /// Token Bucket: tokens are added at a fixed rate and consumed per request.
    /// Allows short bursts up to <c>BucketCapacity</c>.
    /// </summary>
    TokenBucket = 0,

    /// <summary>
    /// Sliding Window: counts calls in a rolling time window.
    /// Smoother than fixed windows; no burst allowance beyond <c>PermitLimit</c>.
    /// </summary>
    SlidingWindow = 1
}
