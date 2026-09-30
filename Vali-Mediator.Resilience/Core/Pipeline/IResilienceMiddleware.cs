using Vali_Mediator_Resilience.Core.Context;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>One resilience policy expressed as a layer around the next step of the pipeline.</summary>
internal interface IResilienceMiddleware
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> next,
        ResilienceContext context,
        CancellationToken cancellationToken);

    /// <summary>True when the caller's own cancellation ended the call (no verdict on the protected dependency).</summary>
    static bool IsCallerCancellation(Exception ex, CancellationToken callerToken) =>
        ex is OperationCanceledException && callerToken.IsCancellationRequested;
}
