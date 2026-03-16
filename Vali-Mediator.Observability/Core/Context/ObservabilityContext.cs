namespace Vali_Mediator_Observability.Core.Context;

/// <summary>
/// Contains runtime context information about a single Vali-Mediator request execution.
/// An instance is created per execution and passed to all registered <c>IRequestObserver</c> implementations.
/// </summary>
public sealed class ObservabilityContext
{
    /// <summary>
    /// The simple type name of the request (e.g. <c>"CreateOrderCommand"</c>).
    /// Derived from <c>typeof(TRequest).Name</c>.
    /// </summary>
    public string RequestName { get; init; } = string.Empty;

    /// <summary>
    /// A unique identifier for this specific execution, represented as a <see cref="Guid"/> string.
    /// Useful for correlating log entries across observers.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// The UTC timestamp when the request execution started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// The elapsed time of the request execution.
    /// Set after the handler returns (either successfully or with an exception).
    /// <c>null</c> during <c>OnStarted</c>.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// <c>true</c> if the request completed without throwing an exception; otherwise <c>false</c>.
    /// Set after the handler returns.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// The exception thrown by the request handler, if any.
    /// <c>null</c> on success or during <c>OnStarted</c>.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// The original request object passed into the mediator.
    /// May be <c>null</c> for dispatch-only scenarios.
    /// </summary>
    public object? Request { get; init; }

    /// <summary>
    /// The response returned by the request handler.
    /// Set after successful completion; <c>null</c> during <c>OnStarted</c> or on failure.
    /// </summary>
    public object? Response { get; set; }

    /// <summary>
    /// An extensible dictionary of arbitrary key-value pairs that observers or behaviors may attach.
    /// Use this to carry cross-cutting metadata (e.g. user id, tenant, correlation id).
    /// </summary>
    public Dictionary<string, object?> Tags { get; } = new Dictionary<string, object?>();
}
