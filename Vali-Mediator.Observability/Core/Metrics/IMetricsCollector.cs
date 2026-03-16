namespace Vali_Mediator_Observability.Core.Metrics;

/// <summary>
/// Defines a pluggable metrics sink for Vali-Mediator request telemetry.
/// Register a concrete implementation via <c>services.AddMetricsCollector&lt;T&gt;()</c>
/// to integrate with any metrics back-end (Prometheus, Datadog, Application Insights, etc.).
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Called when a request execution begins.
    /// </summary>
    /// <param name="requestName">The simple type name of the request.</param>
    void RecordRequestStarted(string requestName);

    /// <summary>
    /// Called when a request execution completes (success or failure resolved).
    /// </summary>
    /// <param name="requestName">The simple type name of the request.</param>
    /// <param name="duration">Total elapsed time of the execution.</param>
    /// <param name="success"><c>true</c> if the handler returned without throwing.</param>
    void RecordRequestCompleted(string requestName, TimeSpan duration, bool success);

    /// <summary>
    /// Called when the request handler throws an unhandled exception.
    /// </summary>
    /// <param name="requestName">The simple type name of the request.</param>
    /// <param name="duration">Total elapsed time until the exception was thrown.</param>
    /// <param name="exceptionType">The full name of the exception type (e.g. <c>"System.InvalidOperationException"</c>).</param>
    void RecordRequestFailed(string requestName, TimeSpan duration, string exceptionType);
}
