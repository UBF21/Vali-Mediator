using Vali_Mediator_Observability.Core.Abstractions;
using Vali_Mediator_Observability.Core.Context;

namespace Vali_Mediator_Observability.Observers;

/// <summary>
/// An <see cref="IRequestObserver"/> that writes structured log output to <see cref="Console"/>.
/// Intended for development and debugging. Register via <c>services.AddConsoleLoggingObserver()</c>.
/// </summary>
public sealed class ConsoleLoggingObserver : IRequestObserver
{
    /// <inheritdoc />
    public Task OnStarted(ObservabilityContext context, CancellationToken ct = default)
    {
        Console.WriteLine(
            $"[Vali-Mediator] START     | OperationId: {context.OperationId} | Request: {context.RequestName} | StartedAt: {context.StartedAt:O}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCompleted(ObservabilityContext context, CancellationToken ct = default)
    {
        Console.WriteLine(
            $"[Vali-Mediator] COMPLETED | OperationId: {context.OperationId} | Request: {context.RequestName} | Duration: {context.Duration?.TotalMilliseconds:F2} ms | Success: {context.IsSuccess}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnFailed(ObservabilityContext context, CancellationToken ct = default)
    {
        Console.WriteLine(
            $"[Vali-Mediator] FAILED    | OperationId: {context.OperationId} | Request: {context.RequestName} | Duration: {context.Duration?.TotalMilliseconds:F2} ms | Exception: {context.Exception?.GetType().Name}: {context.Exception?.Message}");
        return Task.CompletedTask;
    }
}
