namespace Vali_Mediator_Observability.Core.Metrics;

/// <summary>
/// An <see cref="IMetricsCollector"/> implementation that writes metrics to <see cref="Console"/>.
/// Intended for development and debugging. Register via <c>services.AddConsoleMetrics()</c>.
/// </summary>
public sealed class ConsoleMetricsCollector : IMetricsCollector
{
    /// <inheritdoc />
    public void RecordRequestStarted(string requestName)
    {
        Console.WriteLine($"[Vali-Mediator.Metrics] STARTED  | Request: {requestName}");
    }

    /// <inheritdoc />
    public void RecordRequestCompleted(string requestName, TimeSpan duration, bool success)
    {
        var status = success ? "SUCCESS" : "FAILURE";
        Console.WriteLine(
            $"[Vali-Mediator.Metrics] COMPLETED | Request: {requestName} | Status: {status} | Duration: {duration.TotalMilliseconds:F2} ms");
    }

    /// <inheritdoc />
    public void RecordRequestFailed(string requestName, TimeSpan duration, string exceptionType)
    {
        Console.WriteLine(
            $"[Vali-Mediator.Metrics] FAILED    | Request: {requestName} | Exception: {exceptionType} | Duration: {duration.TotalMilliseconds:F2} ms");
    }
}
