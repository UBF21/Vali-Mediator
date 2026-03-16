using Vali_Mediator_Observability.Core.Context;

namespace Vali_Mediator_Observability.Core.Abstractions;

/// <summary>
/// Defines lifecycle hooks that are invoked during the execution of a Vali-Mediator request.
/// Implement this interface to observe request start, completion, and failure events.
/// Multiple observers may be registered; all will be invoked for each event.
/// </summary>
public interface IRequestObserver
{
    /// <summary>
    /// Called immediately before the request handler (and any remaining pipeline behaviors) are invoked.
    /// </summary>
    /// <param name="context">The observability context for this execution.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    Task OnStarted(ObservabilityContext context, CancellationToken ct = default);

    /// <summary>
    /// Called after the request handler completes successfully.
    /// At this point <see cref="ObservabilityContext.Duration"/>, <see cref="ObservabilityContext.IsSuccess"/>,
    /// and <see cref="ObservabilityContext.Response"/> are populated.
    /// </summary>
    /// <param name="context">The observability context for this execution.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    Task OnCompleted(ObservabilityContext context, CancellationToken ct = default);

    /// <summary>
    /// Called when the request handler throws an unhandled exception.
    /// At this point <see cref="ObservabilityContext.Duration"/>, <see cref="ObservabilityContext.Exception"/>,
    /// and <see cref="ObservabilityContext.IsSuccess"/> (false) are populated.
    /// </summary>
    /// <param name="context">The observability context for this execution.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    Task OnFailed(ObservabilityContext context, CancellationToken ct = default);
}
