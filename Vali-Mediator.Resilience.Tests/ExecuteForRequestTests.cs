using Vali_Mediator_Resilience.Core.Exceptions;
using Vali_Mediator_Resilience.Core.Policies;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class ExecuteForRequestTests
{
    private sealed record Login(string User);

    private static ResiliencePolicy PartitionedPolicy() => ResiliencePolicy.Create()
        .RateLimiter(o =>
        {
            o.BucketCapacity = 1;
            o.TokensPerInterval = 0;
            o.ReplenishmentInterval = TimeSpan.FromHours(1);
            o.PartitionKeyResolver = r => ((Login)r).User;
        })
        .Build();

    [Fact]
    public async Task PartitionKeyResolver_WorksWhenThePolicyIsUsedDirectly()
    {
        var policy = PartitionedPolicy();

        Assert.Equal(1, await policy.ExecuteForRequestAsync(new Login("ana"), _ => Task.FromResult(1)));
        Assert.Equal(2, await policy.ExecuteForRequestAsync(new Login("bob"), _ => Task.FromResult(2)));
        await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            policy.ExecuteForRequestAsync(new Login("ana"), _ => Task.FromResult(3)));
    }

    [Fact]
    public async Task VoidVariant_UsesTheSamePartitions()
    {
        var policy = PartitionedPolicy();
        var runs = 0;

        await policy.ExecuteForRequestAsync(new Login("ana"), _ => { runs++; return Task.CompletedTask; });
        await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            policy.ExecuteForRequestAsync(new Login("ana"), _ => { runs++; return Task.CompletedTask; }));

        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task NullRequest_ThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            PartitionedPolicy().ExecuteForRequestAsync<int>(null!, _ => Task.FromResult(1)));
    }

    [Fact]
    public async Task PlainExecuteAsync_WithPartitionResolver_ExplainsHowToFix()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PartitionedPolicy().ExecuteAsync<int>(_ => Task.FromResult(1)));

        Assert.Contains("ExecuteForRequestAsync", ex.Message);
    }
}
