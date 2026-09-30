using Vali_Mediator_Observability.Core.Context;
using Vali_Mediator_Observability.Core.Metrics;
using Vali_Mediator_Observability.Core.Options;
using Vali_Mediator_Observability.Observers;
using Xunit;

namespace Vali_Mediator_Observability.Tests;

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection { }

// Console.SetOut is process-wide, so these tests run alone.
[Collection("Console")]
public class ConsoleOutputTests
{
    private static string Capture(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }

    private static ObservabilityContext FailedContext() => new ObservabilityContext
    {
        RequestName = "Pay",
        OperationId = "op-1",
        Duration = TimeSpan.FromMilliseconds(5),
        Exception = new InvalidOperationException("card 4111-1111-1111-1111 declined")
    };

    [Fact]
    public void ConsoleMetricsCollector_RecordObserverError_PrintsTypeAndHookButNotMessage()
    {
        var output = Capture(() => new ConsoleMetricsCollector().RecordObserverError(
            "My.Observer", "OnStarted", new InvalidOperationException("secret")));

        Assert.Contains("OBSERVER-ERROR", output);
        Assert.Contains("My.Observer", output);
        Assert.Contains("OnStarted", output);
        Assert.Contains(typeof(InvalidOperationException).FullName!, output);
        Assert.DoesNotContain("secret", output);
    }

    [Fact]
    public void ConsoleMetricsCollector_AllHooks_WriteOneLineEach()
    {
        var output = Capture(() =>
        {
            var collector = new ConsoleMetricsCollector();
            collector.RecordRequestStarted("Req");
            collector.RecordRequestCompleted("Req", TimeSpan.FromMilliseconds(3), success: true);
            collector.RecordRequestFailed("Req", TimeSpan.FromMilliseconds(3), "System.Exception");
        });

        Assert.Contains("STARTED", output);
        Assert.Contains("COMPLETED", output);
        Assert.Contains("SUCCESS", output);
        Assert.Contains("FAILED", output);
        Assert.Equal(3, output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void ConsoleLoggingObserver_OnFailed_DefaultOptions_HidesExceptionMessage()
    {
        var output = Capture(() => new ConsoleLoggingObserver().OnFailed(FailedContext()).GetAwaiter().GetResult());

        Assert.Contains("FAILED", output);
        Assert.Contains("InvalidOperationException", output);
        Assert.DoesNotContain("4111", output);
    }

    [Fact]
    public void ConsoleLoggingObserver_OnFailed_IncludeExceptionMessage_ShowsMessage()
    {
        var observer = new ConsoleLoggingObserver(new ObservabilityOptions { IncludeExceptionMessage = true });

        var output = Capture(() => observer.OnFailed(FailedContext()).GetAwaiter().GetResult());

        Assert.Contains("card 4111-1111-1111-1111 declined", output);
    }

    [Fact]
    public void ConsoleLoggingObserver_OnStartedAndCompleted_WriteOperationId()
    {
        var context = new ObservabilityContext { RequestName = "Pay", OperationId = "op-9", IsSuccess = true };
        var observer = new ConsoleLoggingObserver();

        var output = Capture(() =>
        {
            observer.OnStarted(context).GetAwaiter().GetResult();
            observer.OnCompleted(context).GetAwaiter().GetResult();
        });

        Assert.Contains("START", output);
        Assert.Contains("COMPLETED", output);
        Assert.Equal(2, output.Split("op-9").Length - 1);
    }

    [Fact]
    public void ConsoleLoggingObserver_OnFailed_WithoutException_DoesNotThrow()
    {
        var context = new ObservabilityContext { RequestName = "Pay", OperationId = "op-1" };

        var output = Capture(() => new ConsoleLoggingObserver().OnFailed(context).GetAwaiter().GetResult());

        Assert.Contains("(none)", output);
    }
}
