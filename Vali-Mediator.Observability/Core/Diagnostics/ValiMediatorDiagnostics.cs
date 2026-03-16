using System.Diagnostics;

namespace Vali_Mediator_Observability.Core.Diagnostics;

/// <summary>
/// Provides the <see cref="System.Diagnostics.ActivitySource"/> used by Vali-Mediator for distributed tracing.
/// Compatible with any OpenTelemetry SDK that listens to <c>"Vali-Mediator"</c> without requiring
/// an OpenTelemetry dependency in this package.
/// </summary>
/// <remarks>
/// To enable tracing with the OpenTelemetry SDK, add the source name to your tracer provider:
/// <code>
/// using OpenTelemetry.Trace;
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(b => b.AddSource("Vali-Mediator"));
/// </code>
/// </remarks>
public static class ValiMediatorDiagnostics
{
    /// <summary>
    /// The <see cref="System.Diagnostics.ActivitySource"/> for all Vali-Mediator operations.
    /// Source name: <c>"Vali-Mediator"</c>, version: <c>"2.0.0"</c>.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new ActivitySource("Vali-Mediator", "2.0.0");

    /// <summary>
    /// Starts a new <see cref="Activity"/> with the given request name if any listener is active.
    /// Returns <c>null</c> when no listener is attached (zero overhead in production without a tracer).
    /// </summary>
    /// <param name="requestName">The display name for the activity, typically the request type name.</param>
    /// <returns>A started <see cref="Activity"/>, or <c>null</c> if no listener is registered.</returns>
    public static Activity? StartActivity(string requestName)
        => ActivitySource.StartActivity(requestName, ActivityKind.Internal);
}
