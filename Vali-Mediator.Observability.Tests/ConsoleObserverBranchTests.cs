using Vali_Mediator_Observability.Core.Context;
using Vali_Mediator_Observability.Core.Options;
using Vali_Mediator_Observability.Observers;
using Xunit;

namespace Vali_Mediator_Observability.Tests;

[Collection("Console")]
public class ConsoleObserverBranchTests
{
    private static string Capture(Func<Task> action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action().GetAwaiter().GetResult();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }

    [Fact]
    public void OnCompleted_WithoutDuration_StillWritesTheLine()
    {
        var context = new ObservabilityContext { RequestName = "Ping", OperationId = "op-9", IsSuccess = true };

        var output = Capture(() => new ConsoleLoggingObserver().OnCompleted(context));

        Assert.Contains("COMPLETED", output);
        Assert.Contains("op-9", output);
        Assert.Contains("Ping", output);
    }

    [Fact]
    public void OnCompleted_WithDuration_PrintsSuccessAndDuration()
    {
        var context = new ObservabilityContext
        {
            RequestName = "Ping",
            OperationId = "op-8",
            IsSuccess = true,
            Duration = TimeSpan.FromMilliseconds(12)
        };

        var output = Capture(() => new ConsoleLoggingObserver().OnCompleted(context));

        Assert.Contains("Success: True", output);
        Assert.Contains("12", output);
    }

    [Fact]
    public void OnStarted_WritesOperationAndRequest()
    {
        var context = new ObservabilityContext { RequestName = "Ping", OperationId = "op-10" };

        var output = Capture(() => new ConsoleLoggingObserver().OnStarted(context));

        Assert.Contains("START", output);
        Assert.Contains("op-10", output);
    }

    [Fact]
    public void OnFailed_WithoutException_PrintsNone()
    {
        var context = new ObservabilityContext { RequestName = "Ping", OperationId = "op-11" };

        var output = Capture(() => new ConsoleLoggingObserver().OnFailed(context));

        Assert.Contains("(none)", output);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void OnFailed_MessageIsPrintedOnlyWhenTheOptionAllowsIt(bool includeMessage, bool expectMessage)
    {
        var context = new ObservabilityContext
        {
            RequestName = "Pay",
            OperationId = "op-12",
            Exception = new InvalidOperationException("token=abc123")
        };
        var observer = new ConsoleLoggingObserver(new ObservabilityOptions { IncludeExceptionMessage = includeMessage });

        var output = Capture(() => observer.OnFailed(context));

        Assert.Contains("InvalidOperationException", output);
        Assert.Equal(expectMessage, output.Contains("token=abc123"));
    }
}
