using System.Diagnostics;

namespace Vali_Mediator_Resilience.Core.Pipeline;

/// <summary>
/// Wall-clock-shaped time that only moves forward: immune to NTP corrections and manual clock changes.
/// </summary>
internal static class MonotonicClock
{
    private static readonly DateTimeOffset Origin = DateTimeOffset.UtcNow;
    private static readonly long StartTimestamp = Stopwatch.GetTimestamp();

    internal static DateTimeOffset Now => Origin + Stopwatch.GetElapsedTime(StartTimestamp);
}
