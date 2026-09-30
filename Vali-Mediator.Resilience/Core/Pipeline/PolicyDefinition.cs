using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Registry;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>Immutable bundle of the options a <see cref="ResiliencePipeline"/> is composed from.</summary>
internal sealed class PolicyDefinition
{
    internal RetryOptions? Retry { get; init; }
    internal CircuitBreakerOptions? CircuitBreaker { get; init; }
    internal TimeoutOptions? Timeout { get; init; }
    internal BulkheadOptions? Bulkhead { get; init; }
    internal HedgeOptions? Hedge { get; init; }
    internal RateLimiterOptions? RateLimiter { get; init; }
    internal ChaosOptions? Chaos { get; init; }
    internal ICircuitBreakerRegistry Registry { get; init; } = null!;
    internal string? StateKey { get; init; }
}
