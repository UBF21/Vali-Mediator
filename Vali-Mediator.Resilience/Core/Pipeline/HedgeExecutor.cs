using System.Runtime.ExceptionServices;
using Vali_Mediator_Resilience.Core.Context;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>Adapts <see cref="HedgeExecutor"/> to the middleware chain.</summary>
internal sealed class HedgeMiddleware : IResilienceMiddleware
{
    private readonly HedgeOptions _options;

    internal HedgeMiddleware(HedgeOptions options)
    {
        _options = options;
    }

    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> next,
        ResilienceContext context,
        CancellationToken cancellationToken)
        => HedgeExecutor.ExecuteAsync(next, _options, context, cancellationToken);
}

/// <summary>
/// Executes a hedged call: fires the original operation and, every <see cref="HedgeOptions.HedgeDelay"/>,
/// launches up to <see cref="HedgeOptions.MaxHedgedAttempts"/> additional parallel calls.
/// The first acceptable result wins and is returned immediately: the losing attempts are cancelled but
/// NOT awaited, so an operation that ignores its <see cref="CancellationToken"/> cannot delay the winner.
/// If every attempt fails, the last failure is rethrown (or the last rejected result returned).
/// </summary>
internal static class HedgeExecutor
{
    private readonly record struct Attempt<T>(T Result, bool Accepted, Exception? Exception, bool Fatal);

    internal static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        HedgeOptions options,
        ResilienceContext context,
        CancellationToken cancellationToken)
    {
        using var winnerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task<Attempt<T>>> { AttemptAsync(operation, options, winnerCts.Token) };
        int hedgesFired = 0;
        Task? delayTask = options.MaxHedgedAttempts > 0 ? Task.Delay(options.HedgeDelay, winnerCts.Token) : null;
        Attempt<T> last = default;

        try
        {
            while (pending.Count > 0 || delayTask != null)
            {
                var waitOn = new List<Task>(pending);
                if (delayTask != null) waitOn.Add(delayTask);
                var done = await Task.WhenAny(waitOn).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                if (done == delayTask)
                {
                    hedgesFired++;
                    if (options.OnHedge != null)
                        await options.OnHedge(context.CloneForAttempt(hedgesFired)).ConfigureAwait(false);
                    pending.Add(AttemptAsync(operation, options, winnerCts.Token));
                    delayTask = hedgesFired < options.MaxHedgedAttempts
                        ? Task.Delay(options.HedgeDelay, winnerCts.Token)
                        : null;
                    continue;
                }

                var finished = (Task<Attempt<T>>)done;
                pending.Remove(finished);
                last = await finished.ConfigureAwait(false);

                if (last.Accepted) return last.Result;
                if (last.Fatal) ExceptionDispatchInfo.Capture(last.Exception!).Throw();
            }
        }
        finally
        {
            // AttemptAsync never throws, so abandoned losers cannot surface unobserved exceptions.
            // Cancelling before the using-dispose also signals every abandoned attempt.
            winnerCts.Cancel();
        }

        if (last.Exception != null) ExceptionDispatchInfo.Capture(last.Exception).Throw();
        return last.Result;
    }

    private static async Task<Attempt<T>> AttemptAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        HedgeOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await operation(cancellationToken).ConfigureAwait(false);
            bool accepted = options.ShouldHedgeOnResult == null || !options.ShouldHedgeOnResult(result);
            return new Attempt<T>(result, accepted, null, false);
        }
        catch (OperationCanceledException ex)
        {
            return new Attempt<T>(default!, false, ex, false);
        }
        catch (Exception ex)
        {
            bool fatal = !(options.ShouldHedgeOnException?.Invoke(ex) ?? true);
            return new Attempt<T>(default!, false, ex, fatal);
        }
    }
}
