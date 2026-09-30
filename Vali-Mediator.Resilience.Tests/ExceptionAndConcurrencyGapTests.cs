using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Pipeline;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class ResilienceExceptionTests
{
    [Fact]
    public void CircuitOpenException_CarriesTheKeyAndTheRetryHint()
    {
        var ex = new CircuitOpenException("payments", TimeSpan.FromSeconds(12));

        Assert.Equal("payments", ex.CircuitKey);
        Assert.Equal(TimeSpan.FromSeconds(12), ex.RetryAfter);
        Assert.Contains("payments", ex.Message);
    }

    [Fact]
    public void CircuitOpenException_RetryAfterIsOptional()
    {
        var ex = new CircuitOpenException("payments");

        Assert.Null(ex.RetryAfter);
    }

    [Fact]
    public void CircuitOpenException_AcceptsACustomMessage()
    {
        var ex = new CircuitOpenException("payments", "custom text");

        Assert.Equal("custom text", ex.Message);
        Assert.Equal("payments", ex.CircuitKey);
        Assert.Null(ex.RetryAfter);
    }
}

public class PartitionedRateLimiterConcurrencyTests
{
    [Fact]
    public async Task ManyThreadsAskingForTheSameKeyShareOneLimiter()
    {
        var partitioned = new PartitionedRateLimiterState(new RateLimiterOptions { MaxPartitions = 100 });
        using var gate = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            gate.Wait();
            return partitioned.GetOrCreate("shared");
        })).ToArray();
        gate.Set();
        var states = await Task.WhenAll(tasks);

        Assert.All(states, s => Assert.Same(states[0], s));
        Assert.Equal(1, partitioned.Count);
        partitioned.Dispose();
    }

    [Fact]
    public async Task ConcurrentUniqueKeysNeverExceedTheConfiguredBound()
    {
        var partitioned = new PartitionedRateLimiterState(new RateLimiterOptions { MaxPartitions = 4 });
        using var gate = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 64).Select(i => Task.Run(() =>
        {
            gate.Wait();
            return partitioned.GetOrCreate("key-" + i);
        })).ToArray();
        gate.Set();
        await Task.WhenAll(tasks);

        Assert.InRange(partitioned.Count, 1, 4);
        partitioned.Dispose();
    }
}
