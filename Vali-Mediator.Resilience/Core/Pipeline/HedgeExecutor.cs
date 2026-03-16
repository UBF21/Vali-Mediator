using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>
/// Executes a hedged call: fires the original operation and, after <see cref="HedgeOptions.HedgeDelay"/>,
/// launches up to <see cref="HedgeOptions.MaxHedgedAttempts"/> additional parallel calls.
/// The first acceptable result wins; all others are cancelled.
/// </summary>
internal static class HedgeExecutor
{
    internal static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        HedgeOptions options,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        using var winnerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new List<Task<(T Result, bool IsSuccess, Exception? Exception)>>();

        // Launch original attempt
        tasks.Add(AttemptAsync(operation, 0, options, context, winnerCts.Token));

        int hedgesFired = 0;

        while (true)
        {
            // Wait for the hedge delay or any task to complete, whichever comes first
            var delayTask = Task.Delay(options.HedgeDelay, winnerCts.Token);
            var completedTask = await Task.WhenAny(
                Task.WhenAny(tasks),
                delayTask).ConfigureAwait(false);

            // Check if any attempt already finished
            foreach (var t in tasks.ToList())
            {
                if (!t.IsCompleted) continue;

                var attempt = await t.ConfigureAwait(false);

                if (attempt.IsSuccess &&
                    (options.ShouldHedgeOnResult == null || !options.ShouldHedgeOnResult(attempt.Result)))
                {
                    // We have a winner — cancel all other attempts
                    winnerCts.Cancel();
                    return attempt.Result;
                }
            }

            // Fire another hedge if we haven't reached the limit
            if (hedgesFired < options.MaxHedgedAttempts && !winnerCts.Token.IsCancellationRequested)
            {
                hedgesFired++;
                context.AttemptNumber = hedgesFired;
                if (options.OnHedge != null)
                    await options.OnHedge(context).ConfigureAwait(false);

                tasks.Add(AttemptAsync(operation, hedgesFired, options, context, winnerCts.Token));
            }
            else if (tasks.All(t => t.IsCompleted))
            {
                // All attempts done with no winner — return last result or rethrow
                break;
            }
        }

        // No successful result — return last non-exceptional result or rethrow
        await Task.WhenAll(tasks).ConfigureAwait(false);
        var last = tasks.Last();
        var lastResult = await last.ConfigureAwait(false);
        if (lastResult.Exception != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(lastResult.Exception).Throw();
        return lastResult.Result;
    }

    private static async Task<(T Result, bool IsSuccess, Exception? Exception)> AttemptAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int attemptNumber,
        HedgeOptions options,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        _ = attemptNumber; // suppress warning — used in context above
        _ = context;
        try
        {
            if (cancellationToken.IsCancellationRequested)
                return (default!, false, new OperationCanceledException(cancellationToken));

            var result = await operation(cancellationToken).ConfigureAwait(false);
            bool success = options.ShouldHedgeOnResult == null || !options.ShouldHedgeOnResult(result);
            return (result, success, null);
        }
        catch (OperationCanceledException ex)
        {
            return (default!, false, ex);
        }
        catch (Exception ex)
        {
            bool shouldHedge = options.ShouldHedgeOnException?.Invoke(ex) ?? true;
            return (default!, !shouldHedge, shouldHedge ? null : ex);
        }
    }
}
