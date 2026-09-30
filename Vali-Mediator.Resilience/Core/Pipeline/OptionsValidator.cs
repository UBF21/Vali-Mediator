using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>Fails fast, at build time, on option values that could only misbehave at run time.</summary>
internal static class OptionsValidator
{
    internal static void IfSet<T>(T? options, Action<T> validate) where T : class
    {
        if (options != null) validate(options);
    }

    internal static void Validate(RetryOptions o)
    {
        if (o.BackoffType == BackoffType.Custom && o.CustomDelayFactory == null)
            throw new InvalidOperationException("BackoffType.Custom requires RetryOptions.CustomDelayFactory to be set.");

        Require(o.MaxRetries >= 0, nameof(o.MaxRetries), "must be >= 0.");
        Require(o.InitialDelay >= TimeSpan.Zero, nameof(o.InitialDelay), "must not be negative.");
        Require(o.MaxDelay >= TimeSpan.Zero, nameof(o.MaxDelay), "must not be negative.");
        Require(o.Multiplier > 0 && !double.IsNaN(o.Multiplier) && !double.IsInfinity(o.Multiplier),
            nameof(o.Multiplier), "must be a finite number > 0.");
    }

    internal static void Validate(CircuitBreakerOptions o)
    {
        Require(o.FailureThreshold >= 1, nameof(o.FailureThreshold), "must be >= 1.");
        Require(o.FailureRateThreshold >= 0 && o.FailureRateThreshold <= 1,
            nameof(o.FailureRateThreshold), "must be between 0 and 1.");
        Require(o.MinimumThroughput >= 1, nameof(o.MinimumThroughput), "must be >= 1.");
        Require(o.SamplingDuration > TimeSpan.Zero, nameof(o.SamplingDuration), "must be > 0.");
        Require(o.BreakDuration > TimeSpan.Zero, nameof(o.BreakDuration), "must be > 0.");
        Require(o.HalfOpenMaxAttempts >= 1, nameof(o.HalfOpenMaxAttempts), "must be >= 1.");
    }

    internal static void Validate(TimeoutOptions o)
    {
        Require(o.Timeout > TimeSpan.Zero, nameof(o.Timeout), "must be > 0.");
    }

    internal static void Validate(BulkheadOptions o)
    {
        Require(o.MaxConcurrentCalls >= 1, nameof(o.MaxConcurrentCalls), "must be >= 1.");
        Require(o.MaxQueuedCalls >= 0, nameof(o.MaxQueuedCalls), "must be >= 0.");
        Require(o.QueueTimeout >= TimeSpan.Zero || o.QueueTimeout == System.Threading.Timeout.InfiniteTimeSpan,
            nameof(o.QueueTimeout), "must be >= 0 or Timeout.InfiniteTimeSpan.");
    }

    internal static void Validate(HedgeOptions o)
    {
        Require(o.HedgeDelay >= TimeSpan.Zero, nameof(o.HedgeDelay), "must not be negative.");
        Require(o.MaxHedgedAttempts >= 0, nameof(o.MaxHedgedAttempts), "must be >= 0.");
    }

    internal static void Validate(ChaosOptions o)
    {
        Require(o.InjectionRate >= 0 && o.InjectionRate <= 1, nameof(o.InjectionRate), "must be between 0 and 1.");
    }

    internal static void Validate(RateLimiterOptions o)
    {
        Require(o.QueueTimeout >= TimeSpan.Zero, nameof(o.QueueTimeout), "must not be negative.");
        Require(o.MaxPartitions >= 1, nameof(o.MaxPartitions), "must be >= 1.");
        Require(o.PartitionIdleTimeout >= TimeSpan.Zero, nameof(o.PartitionIdleTimeout), "must not be negative.");

        if (o.Algorithm == RateLimiterAlgorithm.SlidingWindow)
        {
            Require(o.PermitLimit >= 0, nameof(o.PermitLimit), "must be >= 0 (0 rejects every call).");
            Require(o.Window > TimeSpan.Zero, nameof(o.Window), "must be > 0.");
        }
        else
        {
            Require(o.BucketCapacity >= 0, nameof(o.BucketCapacity), "must be >= 0 (0 rejects every call).");
            Require(o.TokensPerInterval >= 0, nameof(o.TokensPerInterval), "must be >= 0 (0 disables replenishment).");
            Require(o.ReplenishmentInterval > TimeSpan.Zero, nameof(o.ReplenishmentInterval), "must be > 0.");
        }
    }

    private static void Require(bool condition, string option, string message)
    {
        if (!condition)
            throw new ArgumentOutOfRangeException(option, $"{option} {message}");
    }
}
