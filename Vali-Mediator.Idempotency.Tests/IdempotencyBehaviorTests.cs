using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Idempotency.Core.Interfaces;
using Vali_Mediator_Idempotency.Extension;
using Xunit;

namespace Vali_Mediator_Idempotency.Tests;

public class IdempotencyBehaviorTests
{
    // -------------------------------------------------------------------------
    // Inline request / handler types
    // -------------------------------------------------------------------------

    private class PlainRequest : IRequest<string>
    {
    }

    private class PlainHandler : IRequestHandler<PlainRequest, string>
    {
        private readonly Counter _counter;
        public PlainHandler(Counter counter) => _counter = counter;

        public Task<string> Handle(PlainRequest request, CancellationToken cancellationToken)
        {
            _counter.Value++;
            return Task.FromResult($"response-{_counter.Value}");
        }
    }

    private class IdempotentRequest : IRequest<string>, IIdempotent
    {
        public string IdempotencyKey { get; init; } = string.Empty;
        public TimeSpan? Expiration { get; init; }
    }

    private class IdempotentHandler : IRequestHandler<IdempotentRequest, string>
    {
        private readonly Counter _counter;
        public IdempotentHandler(Counter counter) => _counter = counter;

        public Task<string> Handle(IdempotentRequest request, CancellationToken cancellationToken)
        {
            _counter.Value++;
            return Task.FromResult($"idempotent-response-{_counter.Value}");
        }
    }

    private class AnotherIdempotentRequest : IRequest<int>, IIdempotent
    {
        public string IdempotencyKey { get; init; } = string.Empty;
        public TimeSpan? Expiration { get; init; }
    }

    private class AnotherIdempotentHandler : IRequestHandler<AnotherIdempotentRequest, int>
    {
        private readonly Counter _counter;
        public AnotherIdempotentHandler(Counter counter) => _counter = counter;

        public Task<int> Handle(AnotherIdempotentRequest request, CancellationToken cancellationToken)
        {
            _counter.Value++;
            return Task.FromResult(_counter.Value);
        }
    }

    /// <summary>Simple mutable integer shared via DI.</summary>
    private class Counter
    {
        public int Value { get; set; }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        var counter = new Counter();
        services.AddSingleton(counter);

        services.AddValiMediator(config =>
        {
            config.RegisterServicesFromAssemblyContaining<IdempotencyBehaviorTests>();
            config.AddIdempotencyBehavior();
        });

        services.AddInMemoryIdempotencyStore();

        extra?.Invoke(services);

        return services.BuildServiceProvider();
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NonIdempotentRequest_HandlerCalledEachTime()
    {
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var r1 = await mediator.Send<string>(new PlainRequest());
        var r2 = await mediator.Send<string>(new PlainRequest());

        Assert.NotEqual(r1, r2);

        var counter = provider.GetRequiredService<Counter>();
        Assert.Equal(2, counter.Value);
    }

    [Fact]
    public async Task IdempotentRequest_HandlerCalledOnce_SecondCallReturnsCached()
    {
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var request = new IdempotentRequest { IdempotencyKey = "order-abc-123" };

        var r1 = await mediator.Send<string>(request);
        var r2 = await mediator.Send<string>(request);

        Assert.Equal(r1, r2);

        var counter = provider.GetRequiredService<Counter>();
        Assert.Equal(1, counter.Value);
    }

    [Fact]
    public async Task IdempotentRequest_AfterExpiry_HandlerCalledAgain()
    {
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var request = new IdempotentRequest
        {
            IdempotencyKey = "short-lived-key",
            Expiration = TimeSpan.FromMilliseconds(50)
        };

        var r1 = await mediator.Send<string>(request);

        await Task.Delay(100);

        var r2 = await mediator.Send<string>(request);

        Assert.NotEqual(r1, r2);

        var counter = provider.GetRequiredService<Counter>();
        Assert.Equal(2, counter.Value);
    }

    [Fact]
    public async Task DifferentKeys_EachKeyCallsHandlerIndependently()
    {
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var r1 = await mediator.Send<string>(new IdempotentRequest { IdempotencyKey = "key-A" });
        var r2 = await mediator.Send<string>(new IdempotentRequest { IdempotencyKey = "key-B" });
        var r3 = await mediator.Send<string>(new IdempotentRequest { IdempotencyKey = "key-A" });

        Assert.Equal(r1, r3);
        Assert.NotEqual(r1, r2);

        var counter = provider.GetRequiredService<Counter>();
        Assert.Equal(2, counter.Value);
    }

    [Fact]
    public async Task NonIdempotent_PassesThroughBehaviorTransparently()
    {
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var result = await mediator.Send<string>(new PlainRequest());

        Assert.NotNull(result);
        Assert.StartsWith("response-", result);
    }

    [Fact]
    public async Task IdempotentRequest_NoExpiry_NeverExpires()
    {
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var request = new IdempotentRequest
        {
            IdempotencyKey = "forever-key",
            Expiration = null
        };

        var r1 = await mediator.Send<string>(request);
        var r2 = await mediator.Send<string>(request);
        var r3 = await mediator.Send<string>(request);

        Assert.Equal(r1, r2);
        Assert.Equal(r2, r3);

        var counter = provider.GetRequiredService<Counter>();
        Assert.Equal(1, counter.Value);
    }

    [Fact]
    public async Task DifferentRequestTypes_WithSameKey_AreIndependent()
    {
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var stringResult = await mediator.Send<string>(new IdempotentRequest { IdempotencyKey = "shared-key" });
        var intResult = await mediator.Send<int>(new AnotherIdempotentRequest { IdempotencyKey = "shared-key-int" });

        Assert.NotNull(stringResult);
        Assert.True(intResult > 0);
    }
}
