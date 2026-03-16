namespace Vali_Mediator_Resilience.Core.Enums;

/// <summary>
/// Determines how a timeout is enforced against the executing operation.
/// </summary>
public enum TimeoutStrategy
{
    /// <summary>
    /// Signals the <see cref="System.Threading.CancellationToken"/> passed to the operation.
    /// The operation is expected to observe cancellation and exit cooperatively.
    /// Preferred when you control the operation's implementation.
    /// </summary>
    Optimistic = 0,

    /// <summary>
    /// Uses <see cref="System.Threading.Tasks.Task.WhenAny"/> to race the operation against a
    /// timer task. The operation continues running in the background after the timeout fires,
    /// but the caller receives a <see cref="System.TimeoutException"/> immediately.
    /// Use when you cannot pass a CancellationToken into the delegate.
    /// </summary>
    Pessimistic = 1
}
