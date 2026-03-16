namespace Vali_Mediator.Core.Notification;

/// <summary>
/// Receives notification handler failures that were not re-thrown to the caller.
/// Used with <see cref="PublishStrategy.ResilientParallel"/> to capture individual handler
/// errors without stopping the remaining handlers.
/// </summary>
/// <remarks>
/// Register a custom implementation via DI (<c>services.AddSingleton&lt;IDeadLetterQueue, MyQueue&gt;()</c>)
/// or use the built-in <c>services.AddInMemoryDeadLetterQueue()</c>.
/// When no implementation is registered the mediator silently collects exceptions in an
/// <see cref="AggregateException"/> as before.
/// </remarks>
public interface IDeadLetterQueue
{
    /// <summary>
    /// Called for each handler that failed during a <see cref="PublishStrategy.ResilientParallel"/> publish.
    /// </summary>
    /// <param name="entry">Details about the failure.</param>
    /// <param name="cancellationToken">Propagated from the original publish call.</param>
    Task EnqueueAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes a single notification handler failure captured by the dead-letter queue.
/// </summary>
public sealed class DeadLetterEntry
{
    /// <summary>Full name of the notification type that failed.</summary>
    public string NotificationTypeName { get; init; } = string.Empty;

    /// <summary>Full name of the handler type that threw the exception.</summary>
    public string HandlerTypeName { get; init; } = string.Empty;

    /// <summary>The exception thrown by the handler.</summary>
    public Exception Exception { get; init; } = null!;

    /// <summary>UTC timestamp when the failure occurred.</summary>
    public DateTimeOffset FailedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The notification object (as <c>object</c> for queue agnosticism).</summary>
    public object Notification { get; init; } = null!;
}
