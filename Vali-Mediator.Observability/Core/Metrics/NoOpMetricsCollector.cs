namespace Vali_Mediator_Observability.Core.Metrics;

/// <summary>
/// A no-operation implementation of <see cref="IMetricsCollector"/> that discards all metrics.
/// This is the default collector registered by <c>services.AddObservability()</c>.
/// Replace it with a real implementation via <c>services.AddMetricsCollector&lt;T&gt;()</c>.
/// </summary>
public sealed class NoOpMetricsCollector : IMetricsCollector
{
    /// <inheritdoc />
    public void RecordRequestStarted(string requestName) { }

    /// <inheritdoc />
    public void RecordRequestCompleted(string requestName, TimeSpan duration, bool success) { }

    /// <inheritdoc />
    public void RecordRequestFailed(string requestName, TimeSpan duration, string exceptionType) { }
}
