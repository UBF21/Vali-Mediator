using Vali_Mediator_Observability.Core.Abstractions;
using Vali_Mediator_Observability.Core.Context;
using Vali_Mediator_Observability.Core.Options;

namespace Vali_Mediator_Observability.Observers;

/// <summary>
/// An <see cref="IRequestObserver"/> that writes structured log output to <see cref="Console"/>.
/// Intended for development and debugging. Register via <c>services.AddConsoleLoggingObserver()</c>.
/// </summary>
public sealed class ConsoleLoggingObserver : IRequestObserver
{
    private readonly ObservabilityOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="ConsoleLoggingObserver"/>.
    /// </summary>
    /// <param name="options">Telemetry exposure options; <see cref="ObservabilityOptions"/> defaults when <c>null</c>.</param>
    public ConsoleLoggingObserver(ObservabilityOptions? options = null)
    {
        _options = options ?? new ObservabilityOptions();
    }

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
            $"[Vali-Mediator] FAILED    | OperationId: {context.OperationId} | Request: {context.RequestName} | Duration: {context.Duration?.TotalMilliseconds:F2} ms | Exception: {Describe(context.Exception)}");
        return Task.CompletedTask;
    }

    private string Describe(Exception? ex)
        => ex is null ? "(none)"
            : _options.IncludeExceptionMessage ? $"{ex.GetType().Name}: {ex.Message}" : ex.GetType().Name;
}
