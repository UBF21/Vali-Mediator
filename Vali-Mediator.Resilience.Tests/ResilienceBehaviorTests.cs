using Vali_Mediator.Core.Request;
using Vali_Mediator_Resilience.Core.Policies;
using Vali_Mediator_Resilience.Integration;
using Xunit;

namespace Vali_Mediator_Resilience.Tests;

public class ResilienceBehaviorTests
{
    public sealed record PerRequest(int Retries) : IRequest<int>;

    private sealed class RetriesFromRequestProvider : IResiliencePolicyProvider<PerRequest>
    {
        public ResiliencePolicy GetPolicy(PerRequest request) =>
            ResiliencePolicy.Create()
                .Retry(o => { o.MaxRetries = request.Retries; o.InitialDelay = TimeSpan.FromMilliseconds(1); })
                .Build();
    }

    private sealed class NullProvider : IResiliencePolicyProvider<PerRequest>
    {
        public int Calls;
        public ResiliencePolicy GetPolicy(PerRequest request)
        {
            Calls++;
            return null!;
        }
    }

    private static ResilienceBehavior<PerRequest, int> Create(IResiliencePolicyProvider<PerRequest> provider) =>
        new(new[] { provider }, Array.Empty<IGlobalResiliencePolicyProvider>());

    private static async Task<int> AlwaysFailAttempts(ResilienceBehavior<PerRequest, int> behavior, PerRequest request)
    {
        var attempts = 0;
        await Assert.ThrowsAnyAsync<Exception>(() => behavior.Handle(request, _ =>
        {
            attempts++;
            throw new InvalidOperationException("boom");
        }, CancellationToken.None));
        return attempts;
    }

    [Fact]
    public async Task Handle_SameRequestType_ResolvesPolicyPerRequest()
    {
        var behavior = Create(new RetriesFromRequestProvider());

        var first = await AlwaysFailAttempts(behavior, new PerRequest(1));
        var second = await AlwaysFailAttempts(behavior, new PerRequest(3));

        Assert.Equal(2, first);
        Assert.Equal(4, second);
    }

    [Fact]
    public async Task Handle_NullPolicy_RunsWithoutResilienceAndIsNotCached()
    {
        var provider = new NullProvider();
        var behavior = Create(provider);

        var r1 = await behavior.Handle(new PerRequest(1), _ => Task.FromResult(7), CancellationToken.None);
        var r2 = await behavior.Handle(new PerRequest(1), _ => Task.FromResult(8), CancellationToken.None);

        Assert.Equal(7, r1);
        Assert.Equal(8, r2);
        Assert.Equal(2, provider.Calls);
    }
}
