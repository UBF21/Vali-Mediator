using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Enums;

namespace Vali_Mediator_Resilience.Core.Options;

/// <summary>
/// Configuration for the rate-limiter policy.
/// Controls the rate of outgoing calls using a Token Bucket or Sliding Window algorithm.
/// </summary>
public sealed class RateLimiterOptions
{
    /// <summary>The algorithm used to enforce the rate limit. Default: <see cref="RateLimiterAlgorithm.TokenBucket"/>.</summary>
    public RateLimiterAlgorithm Algorithm { get; set; } = RateLimiterAlgorithm.TokenBucket;

    // ---- Token Bucket ----

    /// <summary>Maximum number of tokens the bucket can hold. Default: 10.</summary>
    public int BucketCapacity { get; set; } = 10;

    /// <summary>Tokens added per replenishment interval. Default: 5.</summary>
    public int TokensPerInterval { get; set; } = 5;

    /// <summary>How often tokens are replenished. Default: 1 s.</summary>
    public TimeSpan ReplenishmentInterval { get; set; } = TimeSpan.FromSeconds(1);

    // ---- Sliding Window ----

    /// <summary>Maximum calls allowed in the sliding window. Default: 100.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Duration of the sliding window. Default: 1 s.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);

    // ---- Common ----

    /// <summary>
    /// How long a caller will wait for a token/slot before being rejected.
    /// Use <see cref="TimeSpan.Zero"/> for fail-fast (immediate rejection when no capacity).
    /// Default: <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>Called when a request is rejected due to rate limiting.</summary>
    public Func<ResilienceContext, Task>? OnRejected { get; set; }
}
