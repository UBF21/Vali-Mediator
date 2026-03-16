using Vali_Mediator_Observability.Core.Metrics;
using Xunit;

namespace Vali_Mediator_Observability.Tests;

public class ConsoleMetricsCollectorTests
{
    private readonly ConsoleMetricsCollector _collector = new ConsoleMetricsCollector();

    [Fact]
    public void RecordRequestStarted_DoesNotThrow()
    {
        var ex = Record.Exception(() => _collector.RecordRequestStarted("TestRequest"));
        Assert.Null(ex);
    }

    [Fact]
    public void RecordRequestCompleted_Success_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _collector.RecordRequestCompleted("TestRequest", TimeSpan.FromMilliseconds(42), success: true));
        Assert.Null(ex);
    }

    [Fact]
    public void RecordRequestCompleted_Failure_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _collector.RecordRequestCompleted("TestRequest", TimeSpan.FromMilliseconds(10), success: false));
        Assert.Null(ex);
    }

    [Fact]
    public void RecordRequestFailed_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _collector.RecordRequestFailed(
                "TestRequest",
                TimeSpan.FromMilliseconds(5),
                "System.InvalidOperationException"));
        Assert.Null(ex);
    }

    [Fact]
    public void NoOpMetricsCollector_AllMethods_DoNotThrow()
    {
        var noop = new NoOpMetricsCollector();

        var exStart = Record.Exception(() => noop.RecordRequestStarted("Req"));
        var exCompleted = Record.Exception(() =>
            noop.RecordRequestCompleted("Req", TimeSpan.Zero, true));
        var exFailed = Record.Exception(() =>
            noop.RecordRequestFailed("Req", TimeSpan.Zero, "System.Exception"));

        Assert.Null(exStart);
        Assert.Null(exCompleted);
        Assert.Null(exFailed);
    }
}
