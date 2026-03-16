namespace Vali_Mediator_Resilience.Core.Exceptions;

/// <summary>
/// Thrown when a request is rejected because the bulkhead's concurrency slot and
/// queue are both full.
/// </summary>
public sealed class BulkheadRejectedException : Exception
{
    /// <summary>Maximum concurrent calls the bulkhead allows.</summary>
    public int MaxConcurrentCalls { get; }

    /// <summary>Maximum number of calls that can wait in the queue.</summary>
    public int MaxQueuedCalls { get; }

    public BulkheadRejectedException(int maxConcurrentCalls, int maxQueuedCalls)
        : base($"Bulkhead rejected the request: max concurrent calls ({maxConcurrentCalls}) " +
               $"and max queued calls ({maxQueuedCalls}) have been reached.")
    {
        MaxConcurrentCalls = maxConcurrentCalls;
        MaxQueuedCalls = maxQueuedCalls;
    }
}
