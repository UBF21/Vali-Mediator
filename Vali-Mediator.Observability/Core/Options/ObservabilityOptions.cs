namespace Vali_Mediator_Observability.Core.Options;

/// <summary>
/// Controls what the observability behaviors and built-in observers may expose in telemetry.
/// Register via <c>services.AddObservability(o =&gt; ...)</c>.
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>
    /// When <c>true</c>, exception messages are written to activity status, <c>observer.error</c> events and
    /// console output. Default <c>false</c>: only the exception type is exposed, because messages often contain
    /// user data, connection strings or other sensitive values.
    /// </summary>
    public bool IncludeExceptionMessage { get; set; }

    internal string Describe(Exception ex)
        => IncludeExceptionMessage ? ex.Message : ex.GetType().FullName ?? ex.GetType().Name;
}
