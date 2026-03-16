using Vali_Mediator_Resilience.Core.Context;

namespace Vali_Mediator_Resilience.Core.Options;

/// <summary>
/// Configuration for bulkhead (concurrency limiter) behaviour.
/// </summary>
public sealed class BulkheadOptions
{
    /// <summary>
    /// Maximum number of calls that can execute concurrently.
    /// Additional calls enter the queue (up to <see cref="MaxQueuedCalls"/>) or are rejected.
    /// Default: 10.
    /// </summary>
    public int MaxConcurrentCalls { get; set; } = 10;

    /// <summary>
    /// Maximum number of calls waiting in the queue when all slots are busy.
    /// 0 means no queue — excess calls are rejected immediately.
    /// Default: 0.
    /// </summary>
    public int MaxQueuedCalls { get; set; } = 0;

    /// <summary>
    /// How long a queued call will wait for a slot before being rejected.
    /// Only relevant when <see cref="MaxQueuedCalls"/> > 0.
    /// Default: <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> (wait forever).
    /// </summary>
    public TimeSpan QueueTimeout { get; set; } = System.Threading.Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Called when a request is rejected because both the concurrency slots and queue are full.
    /// </summary>
    public Func<ResilienceContext, Task>? OnRejected { get; set; }
}
