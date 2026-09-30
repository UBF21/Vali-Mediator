using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Vali_Mediator_Idempotency.Core.Abstractions;
using Vali_Mediator_Idempotency.Core.Exceptions;
using Vali_Mediator_Idempotency.Core.Interfaces;
using Vali_Mediator_Idempotency.Core.Models;
using Vali_Mediator_Idempotency.Core.Options;
using Vali_Mediator_Idempotency.Core.Serialization;
using Vali_Mediator_Idempotency.Core.Store;
using Vali_Mediator_Idempotency.Extension;
using Xunit;

namespace Vali_Mediator_Idempotency.Tests;

/// <summary>Cross-instance reservation: the store contract, the in-memory implementation and the behavior that uses it.</summary>
public class IdempotencyReservationTests
{
    private sealed class Runs
    {
        public int Value;
    }

    private sealed class ReserveReq : IRequest<string>, IIdempotent
    {
        public string IdempotencyKey { get; init; } = string.Empty;
        public TimeSpan? Expiration => TimeSpan.FromMinutes(5);
        public bool Throws { get; init; }
    }

    private sealed class ReserveHandler : IRequestHandler<ReserveReq, string>
    {
        private readonly Runs _runs;
        public ReserveHandler(Runs runs) => _runs = runs;

        public Task<string> Handle(ReserveReq request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs.Value);
            return request.Throws ? throw new InvalidOperationException("boom") : Task.FromResult("fresh");
        }
    }

    private sealed class ReserveResultReq : IRequest<Result<string>>, IIdempotent
    {
        public string IdempotencyKey { get; init; } = string.Empty;
        public TimeSpan? Expiration => TimeSpan.FromMinutes(5);
        public bool Fails { get; init; }
    }

    private sealed class ReserveResultHandler : IRequestHandler<ReserveResultReq, Result<string>>
    {
        private readonly Runs _runs;
        public ReserveResultHandler(Runs runs) => _runs = runs;

        public Task<Result<string>> Handle(ReserveResultReq request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs.Value);
            return Task.FromResult(request.Fails
                ? Result<string>.Fail("nope", ErrorType.Failure)
                : Result<string>.Ok("fresh"));
        }
    }

    /// <summary>Behaves like a store shared with other instances: reservations can be held by "someone else".</summary>
    private sealed class SharedStore : IIdempotencyStore
    {
        public readonly InMemoryIdempotencyStore Inner = new InMemoryIdempotencyStore();
        public int DenyReservations;            // this many TryReserve calls answer "held by another instance"
        public bool DenyForever;
        public int ReserveCalls;
        public readonly List<string> Released = new List<string>();

        public bool SupportsReservation { get; init; } = true;

        public Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct = default) => Inner.FindAsync(key, ct);
        public Task StoreAsync(IdempotencyEntry entry, CancellationToken ct = default) => Inner.StoreAsync(entry, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default) => Inner.RemoveAsync(key, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Inner.ExistsAsync(key, ct);

        public Task<string?> TryReserveAsync(string key, TimeSpan lease, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReserveCalls);
            if (DenyForever || Interlocked.Decrement(ref DenyReservations) >= 0)
                return Task.FromResult<string?>(null);
            return Inner.TryReserveAsync(key, lease, ct);
        }

        public Task ReleaseReservationAsync(string key, string token, CancellationToken ct = default)
        {
            lock (Released) Released.Add(token);
            return Inner.ReleaseReservationAsync(key, token, ct);
        }
    }

    private static ServiceProvider Build(IIdempotencyStore store, Action<IdempotencyOptions>? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Runs());
        services.AddSingleton(store);
        services.AddSingleton<IIdempotencyStore>(sp => sp.GetRequiredService<IdempotencyStoreHolder>().Store);
        services.AddSingleton(new IdempotencyStoreHolder(store));
        services.AddSingleton<IIdempotencySerializer, JsonIdempotencySerializer>();
        services.AddIdempotencyOptions(o =>
        {
            o.ReservationPollInterval = TimeSpan.FromMilliseconds(10);
            options?.Invoke(o);
        });
        services.AddValiMediator(config =>
        {
            config.RegisterServicesFromAssemblyContaining<IdempotencyReservationTests>();
            config.AddIdempotencyBehavior();
        });
        return services.BuildServiceProvider();
    }

    private sealed record IdempotencyStoreHolder(IIdempotencyStore Store);

    private static int Runs_(ServiceProvider p) => p.GetRequiredService<Runs>().Value;

    private static string KeyOf(string id) => typeof(ReserveReq).FullName + "#0:#" + id;

    private static IdempotencyEntry AnswerFor(string id, string answer) => new IdempotencyEntry
    {
        Key = KeyOf(id),
        SerializedResponse = new JsonIdempotencySerializer().Serialize(answer),
        ResponseTypeName = typeof(string).AssemblyQualifiedName!,
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
    };

    // ---- InMemoryIdempotencyStore reservation contract ---------------------------------------------

    [Fact]
    public async Task InMemoryStore_ReservesOnceAndReleasesOnlyWithTheRightToken()
    {
        var store = new InMemoryIdempotencyStore();
        Assert.True(store.SupportsReservation);

        var token = await store.TryReserveAsync("k", TimeSpan.FromMinutes(1));
        Assert.NotNull(token);
        Assert.Null(await store.TryReserveAsync("k", TimeSpan.FromMinutes(1)));
        Assert.NotNull(await store.TryReserveAsync("other", TimeSpan.FromMinutes(1)));

        await store.ReleaseReservationAsync("k", "not-my-token");
        Assert.Null(await store.TryReserveAsync("k", TimeSpan.FromMinutes(1)));

        await store.ReleaseReservationAsync("k", token!);
        Assert.NotNull(await store.TryReserveAsync("k", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task InMemoryStore_ReservationExpiresWithItsLease()
    {
        var time = new ManualTime();
        var store = new InMemoryIdempotencyStore(timeProvider: time);

        var first = await store.TryReserveAsync("k", TimeSpan.FromSeconds(30));
        time.Advance(TimeSpan.FromSeconds(31));
        var second = await store.TryReserveAsync("k", TimeSpan.FromSeconds(30));

        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        await store.ReleaseReservationAsync("k", first!); // the expired holder must not release the new reservation
        Assert.Null(await store.TryReserveAsync("k", TimeSpan.FromSeconds(30)));
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    [Fact]
    public async Task ADefaultStore_KeepsTheLegacyContract()
    {
        IIdempotencyStore store = new LegacyStore(); // default interface members are only reachable through the interface
        Assert.False(store.SupportsReservation);
        Assert.Null(await store.TryReserveAsync("k", TimeSpan.FromMinutes(1)));
        await store.ReleaseReservationAsync("k", "t"); // no-op, must not throw
    }

    private sealed class LegacyStore : IIdempotencyStore
    {
        public Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct = default) => Task.FromResult<IdempotencyEntry?>(null);
        public Task StoreAsync(IdempotencyEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
    }

    // ---- behavior --------------------------------------------------------------------------------------

    [Fact]
    public async Task ALegacyStore_NeverTouchesTheReservationApi()
    {
        var store = new SharedStore { SupportsReservation = false, DenyForever = true };
        await using var provider = Build(store);
        var mediator = provider.GetRequiredService<IValiMediator>();

        Assert.Equal("fresh", await mediator.Send(new ReserveReq { IdempotencyKey = "legacy" }));

        Assert.Equal(0, store.ReserveCalls);
        Assert.Equal(1, Runs_(provider));
    }

    [Fact]
    public async Task WhenAnotherInstanceHoldsTheKey_TheCallerWaitsAndReplaysItsAnswer()
    {
        var store = new SharedStore { DenyForever = true };
        await using var provider = Build(store);
        var mediator = provider.GetRequiredService<IValiMediator>();

        var pending = mediator.Send(new ReserveReq { IdempotencyKey = "wait" });
        await Task.Delay(150);
        Assert.False(pending.IsCompleted); // still waiting for the other instance

        await store.StoreAsync(AnswerFor("wait", "from-the-other-instance"));

        Assert.Equal("from-the-other-instance", await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, Runs_(provider)); // our handler never ran
    }

    [Fact]
    public async Task WhenTheOtherInstanceFreesTheKeyWithoutAnAnswer_TheCallerRunsTheHandlerOnce()
    {
        var store = new SharedStore { DenyReservations = 3 };
        await using var provider = Build(store);
        var mediator = provider.GetRequiredService<IValiMediator>();

        Assert.Equal("fresh", await mediator.Send(new ReserveReq { IdempotencyKey = "freed" }));

        Assert.Equal(1, Runs_(provider));
        Assert.Equal(4, store.ReserveCalls);
        Assert.Single(store.Released);
        Assert.Equal("fresh", await mediator.Send(new ReserveReq { IdempotencyKey = "freed" })); // replayed
        Assert.Equal(1, Runs_(provider));
    }

    [Fact]
    public async Task WhenTheAnswerAppearsBetweenFindAndReserve_ItIsReplayedAndTheReservationReleased()
    {
        var store = new SharedStore();
        await using var provider = Build(store);
        var mediator = provider.GetRequiredService<IValiMediator>();
        await store.StoreAsync(AnswerFor("late", "already-there"));

        Assert.Equal("already-there", await mediator.Send(new ReserveReq { IdempotencyKey = "late" }));

        Assert.Equal(0, Runs_(provider));
    }

    [Fact]
    public async Task StillHeldAfterTheWaitTimeout_ReturnsInProgress()
    {
        var store = new SharedStore { DenyForever = true };
        await using var provider = Build(store, o => o.ReservationWaitTimeout = TimeSpan.FromMilliseconds(120));
        var mediator = provider.GetRequiredService<IValiMediator>();

        var thrown = await Assert.ThrowsAsync<IdempotencyInProgressException>(
            () => mediator.Send(new ReserveReq { IdempotencyKey = "stuck" }));
        Assert.Equal("stuck", thrown.IdempotencyKey);

        var result = await mediator.Send(new ReserveResultReq { IdempotencyKey = "stuck-result" });
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.ErrorType);
        Assert.Equal(0, Runs_(provider));
    }

    [Fact]
    public async Task AHandlerException_ReleasesTheReservationSoARetryCanRun()
    {
        var store = new SharedStore();
        await using var provider = Build(store);
        var mediator = provider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.Send(new ReserveReq { IdempotencyKey = "boom", Throws = true }));
        Assert.Single(store.Released);

        Assert.Equal("fresh", await mediator.Send(new ReserveReq { IdempotencyKey = "boom" }));
        Assert.Equal(2, Runs_(provider));
    }

    [Fact]
    public async Task AFailedResult_ReleasesTheReservationAndIsNotReplayed()
    {
        var store = new SharedStore();
        await using var provider = Build(store);
        var mediator = provider.GetRequiredService<IValiMediator>();

        var failed = await mediator.Send(new ReserveResultReq { IdempotencyKey = "fail", Fails = true });
        var retried = await mediator.Send(new ReserveResultReq { IdempotencyKey = "fail" });

        Assert.True(failed.IsFailure);
        Assert.True(retried.IsSuccess);
        Assert.Equal(2, Runs_(provider));
        Assert.Equal(2, store.Released.Count);
    }

    [Fact]
    public async Task ACancelledCaller_StillReleasesTheReservation()
    {
        var store = new SharedStore();
        await using var provider = Build(store);
        var mediator = provider.GetRequiredService<IValiMediator>();
        using var cts = new CancellationTokenSource();

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => mediator.Send(new ReserveReq { IdempotencyKey = "cancelled" }, cts.Token));

        Assert.Equal("fresh", await mediator.Send(new ReserveReq { IdempotencyKey = "cancelled" })); // not blocked by a leak
    }

    [Fact]
    public async Task ConcurrentCallersSharingOneStore_RunTheHandlerOnce()
    {
        await using var provider = Build(new InMemoryIdempotencyStore());
        var mediator = provider.GetRequiredService<IValiMediator>();

        var results = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(_ => mediator.Send(new ReserveReq { IdempotencyKey = "same" })));

        Assert.All(results, r => Assert.Equal("fresh", r));
        Assert.Equal(1, Runs_(provider));
    }

    // ---- options ----------------------------------------------------------------------------------------

    [Fact]
    public void ReservationOptions_HaveSaneDefaults()
    {
        var options = new IdempotencyOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.ReservationLease);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ReservationWaitTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(25), options.ReservationPollInterval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ReservationOptions_RejectNonPositiveValues(int seconds)
    {
        var options = new IdempotencyOptions();
        var value = TimeSpan.FromSeconds(seconds);

        Assert.Throws<ArgumentOutOfRangeException>(() => options.ReservationLease = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.ReservationWaitTimeout = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.ReservationPollInterval = value);
    }
}
