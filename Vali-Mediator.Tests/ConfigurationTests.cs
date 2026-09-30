using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Xunit;

namespace Vali_Mediator.Tests;

/// <summary>Core limits configured through <c>AddValiMediator</c> / <c>AddInMemoryDeadLetterQueue</c>.</summary>
public class ConfigurationTests
{
    private sealed class CfgRequest : IRequest<int>
    {
        public int Id { get; init; }
    }

    private sealed class ConcurrencyProbe
    {
        private int _current;
        private int _peak;
        public int Peak => Volatile.Read(ref _peak);

        public void Enter()
        {
            var now = Interlocked.Increment(ref _current);
            int seen;
            while (now > (seen = Volatile.Read(ref _peak)) && Interlocked.CompareExchange(ref _peak, now, seen) != seen) { }
        }

        public void Exit() => Interlocked.Decrement(ref _current);
    }

    private sealed class CfgHandler : IRequestHandler<CfgRequest, int>
    {
        private readonly ConcurrencyProbe _probe;
        public CfgHandler(ConcurrencyProbe probe) => _probe = probe;

        public async Task<int> Handle(CfgRequest request, CancellationToken cancellationToken)
        {
            _probe.Enter();
            try
            {
                await Task.Delay(25, cancellationToken);
                return request.Id;
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private static ServiceProvider Build(int? sendAllLimit)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ConcurrencyProbe>();
        services.AddValiMediator(config =>
        {
            config.RegisterServicesFromAssemblyContaining<ConfigurationTests>();
            config.SendAllMaxDegreeOfParallelism = sendAllLimit;
        });
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void SendAllMaxDegreeOfParallelism_RejectsLessThanOne_AtConfigurationTime(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ValiMediatorConfiguration { SendAllMaxDegreeOfParallelism = value });
        Assert.Equal("SendAllMaxDegreeOfParallelism", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(int.MaxValue)]
    public void SendAllMaxDegreeOfParallelism_AcceptsNullAndPositive(int? value)
        => Assert.Equal(value, new ValiMediatorConfiguration { SendAllMaxDegreeOfParallelism = value }.SendAllMaxDegreeOfParallelism);

    [Fact]
    public void SendAllMaxDegreeOfParallelism_DefaultIsUnbounded()
        => Assert.Null(new ValiMediatorConfiguration().SendAllMaxDegreeOfParallelism);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task SendAll_UsesTheConfiguredDefaultLimit(int limit)
    {
        await using var provider = Build(limit);
        var mediator = provider.GetRequiredService<IValiMediator>();
        var requests = Enumerable.Range(0, limit * 4).Select(i => (IRequest<int>)new CfgRequest { Id = i }).ToList();

        var results = await mediator.SendAll(requests);

        Assert.Equal(Enumerable.Range(0, limit * 4), results);
        Assert.InRange(provider.GetRequiredService<ConcurrencyProbe>().Peak, 1, limit);
    }

    [Fact]
    public async Task SendAll_WithoutConfiguredLimit_StartsEveryRequestAtOnce()
    {
        await using var provider = Build(null);
        var mediator = provider.GetRequiredService<IValiMediator>();
        var requests = Enumerable.Range(0, 8).Select(i => (IRequest<int>)new CfgRequest { Id = i }).ToList();

        await mediator.SendAll(requests);

        Assert.True(provider.GetRequiredService<ConcurrencyProbe>().Peak > 1);
    }

    [Fact]
    public async Task SendAll_ExplicitLimitOverridesTheConfiguredDefault()
    {
        await using var provider = Build(1);
        var mediator = provider.GetRequiredService<IValiMediator>();
        var requests = Enumerable.Range(0, 8).Select(i => (IRequest<int>)new CfgRequest { Id = i }).ToList();

        await mediator.SendAll(requests, 4);

        Assert.InRange(provider.GetRequiredService<ConcurrencyProbe>().Peak, 2, 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DeadLetterQueue_MaxEntries_RejectsLessThanOne(int maxEntries)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddInMemoryDeadLetterQueue(maxEntries));

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(1_000)]
    public async Task DeadLetterQueue_MaxEntries_ConfiguredThroughDi_KeepsTheNewestEntries(int maxEntries)
    {
        var services = new ServiceCollection();
        services.AddInMemoryDeadLetterQueue(maxEntries);
        await using var provider = services.BuildServiceProvider();
        var queue = (InMemoryDeadLetterQueue)provider.GetRequiredService<IDeadLetterQueue>();
        var total = Math.Min(maxEntries + 3, 1_010);

        for (var i = 0; i < total; i++)
            await queue.EnqueueAsync(new DeadLetterEntry
            {
                NotificationTypeName = "n" + i,
                HandlerTypeName = "h",
                Exception = new InvalidOperationException(),
                Notification = i
            });

        Assert.Equal(Math.Min(total, maxEntries), queue.Count);
        Assert.Equal("n" + (total - 1), queue.GetEntries()[^1].NotificationTypeName);
    }

    [Fact]
    public void DeadLetterQueue_DefaultCapacityIsUnchanged()
    {
        var queue = new InMemoryDeadLetterQueue();
        for (var i = 0; i < 1_005; i++)
            queue.EnqueueAsync(new DeadLetterEntry { Exception = new Exception(), Notification = i });

        Assert.Equal(1_000, queue.Count);
    }
}
