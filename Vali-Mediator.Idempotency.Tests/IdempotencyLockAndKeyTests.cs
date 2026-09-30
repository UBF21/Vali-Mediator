using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Idempotency.Core.Interfaces;
using Vali_Mediator_Idempotency.Extension;
using Vali_Mediator_Idempotency.Pipeline;
using Xunit;

namespace Vali_Mediator_Idempotency.Tests;

public class IdempotencyLockAndKeyTests
{
    private class Counter
    {
        public int Value;
    }

    private class StringReq : IRequest<string>, IIdempotent
    {
        public string IdempotencyKey { get; init; } = string.Empty;
        public TimeSpan? Expiration { get; init; }
    }

    private class StringHandler : IRequestHandler<StringReq, string>
    {
        private readonly Counter _counter;
        public StringHandler(Counter counter) => _counter = counter;

        public async Task<string> Handle(StringReq request, CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken);
            Interlocked.Increment(ref _counter.Value);
            return "text";
        }
    }

    private class IntReq : IRequest<int>, IIdempotent
    {
        public string IdempotencyKey { get; init; } = string.Empty;
        public TimeSpan? Expiration { get; init; }
    }

    private class IntHandler : IRequestHandler<IntReq, int>
    {
        public Task<int> Handle(IntReq request, CancellationToken cancellationToken) => Task.FromResult(42);
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Counter());
        services.AddValiMediator(config =>
        {
            config.RegisterServicesFromAssemblyContaining<IdempotencyLockAndKeyTests>();
            config.AddIdempotencyBehavior();
        });
        services.AddInMemoryIdempotencyStore();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SameKeyInParallel_ExecutesHandlerOnce()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => mediator.Send<string>(new StringReq { IdempotencyKey = "k-parallel" })));

        Assert.All(results, r => Assert.Equal("text", r));
        Assert.Equal(1, provider.GetRequiredService<Counter>().Value);
    }

    [Fact]
    public async Task DifferentRequestTypes_SameKey_DoNotCollide()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var text = await mediator.Send<string>(new StringReq { IdempotencyKey = "shared" });
        var number = await mediator.Send<int>(new IntReq { IdempotencyKey = "shared" });

        Assert.Equal("text", text);
        Assert.Equal(42, number);
    }

    [Fact]
    public async Task LockDictionary_DoesNotGrowAfterCompletion()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        for (var i = 0; i < 25; i++)
            await mediator.Send<string>(new StringReq { IdempotencyKey = "unique-" + i });

        Assert.Equal(0, IdempotencyBehavior<StringReq, string>.ActiveLockCount);
    }
}
