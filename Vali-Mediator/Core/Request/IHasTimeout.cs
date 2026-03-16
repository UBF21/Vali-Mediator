namespace Vali_Mediator.Core.Request;

/// <summary>
/// Opt-in interface for requests that declare their own per-execution timeout.
/// Implement this on any <c>IRequest&lt;TResponse&gt;</c> and register
/// <c>config.AddTimeoutBehavior()</c> to activate automatic cancellation.
/// </summary>
/// <remarks>
/// The behavior combines the declared timeout with the caller's
/// <see cref="CancellationToken"/> via <see cref="CancellationTokenSource.CreateLinkedTokenSource"/>,
/// so whichever fires first wins.
/// </remarks>
public interface IHasTimeout
{
    /// <summary>
    /// The maximum time allowed for this request's handler to complete.
    /// A <see cref="TimeoutException"/> is thrown if the handler exceeds this limit.
    /// </summary>
    TimeSpan Timeout { get; }
}
