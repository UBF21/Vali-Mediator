using System.Text.RegularExpressions;
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
using Vali_Mediator_Idempotency.Pipeline;
using Xunit;

namespace Vali_Mediator_Idempotency.Tests;

public class IdempotencyHardeningTests
{
    private sealed class Counter
    {
        public int Value;
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class SpyStore : IIdempotencyStore
    {
        public readonly InMemoryIdempotencyStore Inner = new InMemoryIdempotencyStore();
        public IdempotencyEntry? Last;

        public Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct = default) => Inner.FindAsync(key, ct);

        public Task StoreAsync(IdempotencyEntry entry, CancellationToken ct = default)
        {
            Last = entry;
            return Inner.StoreAsync(entry, ct);
        }

        public Task RemoveAsync(string key, CancellationToken ct = default) => Inner.RemoveAsync(key, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Inner.ExistsAsync(key, ct);
    }

    private sealed class ScopedReq : IRequest<string>, IIdempotent
    {
        public string IdempotencyKey { get; init; } = string.Empty;
        public TimeSpan? Expiration { get; init; }
        public string? IdempotencyScope { get; init; }
        public string Payload { get; init; } = string.Empty;
    }

    private sealed class ScopedHandler : IRequestHandler<ScopedReq, string>
    {
        private readonly Counter _counter;
        public ScopedHandler(Counter counter) => _counter = counter;

        public async Task<string> Handle(ScopedReq request, CancellationToken cancellationToken)
        {
            await Task.Delay(20, cancellationToken);
            Interlocked.Increment(ref _counter.Value);
            return "scoped:" + request.Payload;
        }
    }

    private sealed class ResultReq : IRequest<Result<string>>, IIdempotent
    {
        public string IdempotencyKey { get; init; } = string.Empty;
        public TimeSpan? Expiration { get; init; }
        public string Payload { get; init; } = string.Empty;
    }

    private sealed class ResultHandler : IRequestHandler<ResultReq, Result<string>>
    {
        private readonly Counter _counter;
        public ResultHandler(Counter counter) => _counter = counter;

        public async Task<Result<string>> Handle(ResultReq request, CancellationToken cancellationToken)
        {
            await Task.Delay(20, cancellationToken);
            Interlocked.Increment(ref _counter.Value);
            return Result<string>.Ok("result:" + request.Payload);
        }
    }

    private static ServiceProvider Build(
        FakeTime? time = null,
        IIdempotencyStore? store = null,
        Action<IdempotencyOptions>? options = null,
        Action<InMemoryIdempotencyStoreOptions>? storeOptions = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Counter());
        if (time is not null) services.AddSingleton<TimeProvider>(time);
        if (options is not null) services.AddIdempotencyOptions(options);
        services.AddValiMediator(config =>
        {
            config.RegisterServicesFromAssemblyContaining<IdempotencyHardeningTests>();
            config.AddIdempotencyBehavior();
        });
        if (storeOptions is not null) services.AddInMemoryIdempotencyStore(storeOptions);
        else services.AddInMemoryIdempotencyStore();
        if (store is not null) services.AddSingleton(store);
        return services.BuildServiceProvider();
    }

    private static int Count(ServiceProvider p) => p.GetRequiredService<Counter>().Value;

    // ---- AUD-C-01: type name is version independent ------------------------------------------------

    [Fact]
    public async Task StoredEntryWithOldAssemblyVersions_StillReplays()
    {
        var spy = new SpyStore();
        await using var provider = Build(store: spy);
        var mediator = provider.GetRequiredService<IValiMediator>();
        var request = new ResultReq { IdempotencyKey = "old-version", Payload = "p" };

        await mediator.Send(request);
        var stored = spy.Last!;
        var oldName = Regex.Replace(typeof(Result<string>).AssemblyQualifiedName!, @"Version=[\d.]+", "Version=1.0.0.0");
        Assert.NotEqual(typeof(Result<string>).AssemblyQualifiedName, oldName);

        await spy.Inner.StoreAsync(new IdempotencyEntry
        {
            Key = stored.Key,
            SerializedResponse = stored.SerializedResponse,
            ResponseTypeName = oldName,
            RequestFingerprint = stored.RequestFingerprint,
            CreatedAt = stored.CreatedAt,
            ExpiresAt = stored.ExpiresAt
        });

        var replay = await mediator.Send(request);

        Assert.True(replay.IsSuccess);
        Assert.Equal("result:p", replay.Value);
        Assert.Equal(1, Count(provider));
    }

    [Fact]
    public async Task StoredEntryOfAnotherResponseType_IsTreatedAsMiss()
    {
        var spy = new SpyStore();
        await using var provider = Build(store: spy);
        var mediator = provider.GetRequiredService<IValiMediator>();
        var request = new ScopedReq { IdempotencyKey = "wrong-type", Payload = "p" };

        await mediator.Send<string>(request);
        var stored = spy.Last!;
        await spy.Inner.StoreAsync(new IdempotencyEntry
        {
            Key = stored.Key,
            SerializedResponse = stored.SerializedResponse,
            ResponseTypeName = "System.Int32",
            RequestFingerprint = stored.RequestFingerprint,
            CreatedAt = stored.CreatedAt,
            ExpiresAt = stored.ExpiresAt
        });

        await mediator.Send<string>(request);

        Assert.Equal(2, Count(provider));
    }

    // ---- AUD-S-01: scope + payload fingerprint ------------------------------------------------------

    [Fact]
    public async Task SameKeyDifferentScope_DoesNotShareResponse()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var alice = await mediator.Send<string>(new ScopedReq { IdempotencyKey = "k", IdempotencyScope = "alice", Payload = "a" });
        var bob = await mediator.Send<string>(new ScopedReq { IdempotencyKey = "k", IdempotencyScope = "bob", Payload = "b" });

        Assert.Equal("scoped:a", alice);
        Assert.Equal("scoped:b", bob);
        Assert.Equal(2, Count(provider));
    }

    [Fact]
    public async Task SameKeySameScope_Replays()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();
        var request = new ScopedReq { IdempotencyKey = "k", IdempotencyScope = "alice", Payload = "a" };

        await mediator.Send<string>(request);
        await mediator.Send<string>(request);

        Assert.Equal(1, Count(provider));
    }

    [Fact]
    public async Task ScopeAndKeyContainingSeparators_DoNotCollide()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        await mediator.Send<string>(new ScopedReq { IdempotencyKey = "b:c", IdempotencyScope = "a", Payload = "1" });
        await mediator.Send<string>(new ScopedReq { IdempotencyKey = "c", IdempotencyScope = "a:b", Payload = "1" });

        Assert.Equal(2, Count(provider));
    }

    [Fact]
    public async Task SameKeyDifferentPayload_ResultResponse_ReturnsConflictFailure()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var first = await mediator.Send(new ResultReq { IdempotencyKey = "fp", Payload = "a" });
        var second = await mediator.Send(new ResultReq { IdempotencyKey = "fp", Payload = "b" });

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.ErrorType);
        Assert.Equal(1, Count(provider));
    }

    [Fact]
    public async Task SameKeyDifferentPayload_PlainResponse_ThrowsConflictException()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        await mediator.Send<string>(new ScopedReq { IdempotencyKey = "fp", Payload = "a" });
        var ex = await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => mediator.Send<string>(new ScopedReq { IdempotencyKey = "fp", Payload = "b" }));

        Assert.Equal("fp", ex.IdempotencyKey);
        Assert.Equal(0, IdempotencyBehavior<ScopedReq, string>.ActiveLockCount);
    }

    [Fact]
    public async Task FingerprintVerificationDisabled_ReplaysDespiteDifferentPayload()
    {
        await using var provider = Build(options: o => o.VerifyRequestFingerprint = false);
        var mediator = provider.GetRequiredService<IValiMediator>();

        var first = await mediator.Send<string>(new ScopedReq { IdempotencyKey = "fp", Payload = "a" });
        var second = await mediator.Send<string>(new ScopedReq { IdempotencyKey = "fp", Payload = "b" });

        Assert.Equal(first, second);
        Assert.Equal(1, Count(provider));
    }

    // ---- limits: key length ------------------------------------------------------------------------

    [Fact]
    public async Task KeyLongerThanDefaultLimit_IsRejected()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => mediator.Send<string>(new ScopedReq { IdempotencyKey = new string('k', 257) }));
        Assert.Equal(0, Count(provider));
    }

    [Fact]
    public async Task ScopeLongerThanLimit_IsRejected()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => mediator.Send<string>(new ScopedReq { IdempotencyKey = "k", IdempotencyScope = new string('s', 257) }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyKey_IsRejected(string key)
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => mediator.Send<string>(new ScopedReq { IdempotencyKey = key }));
    }

    [Fact]
    public async Task CustomMaxKeyLength_IsHonored()
    {
        await using var provider = Build(options: o => o.MaxKeyLength = 5);
        var mediator = provider.GetRequiredService<IValiMediator>();

        await mediator.Send<string>(new ScopedReq { IdempotencyKey = "12345" });
        await Assert.ThrowsAsync<ArgumentException>(
            () => mediator.Send<string>(new ScopedReq { IdempotencyKey = "123456" }));
    }

    [Fact]
    public void NonPositiveMaxKeyLength_IsRejectedAtConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdempotencyBehavior<ScopedReq, string>(
            new InMemoryIdempotencyStore(), new JsonIdempotencySerializer(), new IdempotencyOptions { MaxKeyLength = 0 }));
    }

    // ---- expiry with a controlled clock ------------------------------------------------------------

    [Fact]
    public async Task ExplicitExpiration_IsHonoredWithFakeClock()
    {
        var time = new FakeTime();
        await using var provider = Build(time);
        var mediator = provider.GetRequiredService<IValiMediator>();
        var request = new ScopedReq { IdempotencyKey = "exp", Expiration = TimeSpan.FromMinutes(1), Payload = "a" };

        await mediator.Send<string>(request);
        time.Advance(TimeSpan.FromSeconds(30));
        await mediator.Send<string>(request);
        Assert.Equal(1, Count(provider));

        time.Advance(TimeSpan.FromSeconds(31));
        await mediator.Send<string>(request);
        Assert.Equal(2, Count(provider));
    }

    [Fact]
    public async Task NullExpiration_UsesStoreDefaultOf24Hours()
    {
        var time = new FakeTime();
        await using var provider = Build(time);
        var mediator = provider.GetRequiredService<IValiMediator>();
        var request = new ScopedReq { IdempotencyKey = "default-exp", Payload = "a" };

        await mediator.Send<string>(request);
        time.Advance(TimeSpan.FromHours(23));
        await mediator.Send<string>(request);
        Assert.Equal(1, Count(provider));

        time.Advance(TimeSpan.FromHours(2));
        await mediator.Send<string>(request);
        Assert.Equal(2, Count(provider));
    }

    // ---- concurrency -------------------------------------------------------------------------------

    [Fact]
    public async Task ParallelSameKeyMixedPayloads_ExecutesHandlerOnce()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            try
            {
                return await mediator.Send<string>(new ScopedReq { IdempotencyKey = "mix", Payload = i % 2 == 0 ? "a" : "b" });
            }
            catch (IdempotencyConflictException)
            {
                return "conflict";
            }
        }));

        Assert.Equal(1, Count(provider));
        Assert.Equal(20, results.Count(r => r == "conflict") + results.Count(r => r.StartsWith("scoped:")));
        Assert.Equal(0, IdempotencyBehavior<ScopedReq, string>.ActiveLockCount);
    }

    [Fact]
    public async Task ParallelDistinctScopes_EachExecutesOnce()
    {
        await using var provider = Build();
        var mediator = provider.GetRequiredService<IValiMediator>();

        await Task.WhenAll(Enumerable.Range(0, 10).SelectMany(i => new[]
        {
            mediator.Send<string>(new ScopedReq { IdempotencyKey = "same", IdempotencyScope = "u" + i, Payload = "x" }),
            mediator.Send<string>(new ScopedReq { IdempotencyKey = "same", IdempotencyScope = "u" + i, Payload = "x" })
        }));

        Assert.Equal(10, Count(provider));
    }

    // ---- store limits ------------------------------------------------------------------------------

    private static IdempotencyEntry Entry(string key, DateTimeOffset? expires = null)
        => new IdempotencyEntry
        {
            Key = key,
            SerializedResponse = new byte[] { 1 },
            ResponseTypeName = "System.String",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expires
        };

    [Fact]
    public async Task Store_EvictsOldestBeyondMaxEntries()
    {
        var store = new InMemoryIdempotencyStore(new InMemoryIdempotencyStoreOptions { MaxEntries = 3 });

        for (var i = 1; i <= 5; i++)
            await store.StoreAsync(Entry("k" + i));

        Assert.False(await store.ExistsAsync("k1"));
        Assert.False(await store.ExistsAsync("k2"));
        Assert.True(await store.ExistsAsync("k3"));
        Assert.True(await store.ExistsAsync("k4"));
        Assert.True(await store.ExistsAsync("k5"));
    }

    [Fact]
    public async Task Store_RewritingAKey_MakesItTheNewest()
    {
        var store = new InMemoryIdempotencyStore(new InMemoryIdempotencyStoreOptions { MaxEntries = 2 });

        await store.StoreAsync(Entry("a"));
        await store.StoreAsync(Entry("b"));
        await store.StoreAsync(Entry("a"));
        await store.StoreAsync(Entry("c"));

        Assert.True(await store.ExistsAsync("a"));
        Assert.False(await store.ExistsAsync("b"));
        Assert.True(await store.ExistsAsync("c"));
    }

    [Fact]
    public async Task Store_AppliesDefaultExpirationOnlyToEntriesWithoutOne()
    {
        var time = new FakeTime();
        var store = new InMemoryIdempotencyStore(
            new InMemoryIdempotencyStoreOptions { DefaultExpiration = TimeSpan.FromHours(1) }, time);

        await store.StoreAsync(Entry("default"));
        await store.StoreAsync(Entry("explicit", time.GetUtcNow().AddDays(2)));
        time.Advance(TimeSpan.FromHours(2));

        Assert.False(await store.ExistsAsync("default"));
        Assert.True(await store.ExistsAsync("explicit"));
    }

    [Fact]
    public async Task Store_WithNullDefaultExpiration_KeepsEntriesUntilEvicted()
    {
        var time = new FakeTime();
        var store = new InMemoryIdempotencyStore(
            new InMemoryIdempotencyStoreOptions { DefaultExpiration = null }, time);

        await store.StoreAsync(Entry("forever"));
        time.Advance(TimeSpan.FromDays(3650));

        Assert.True(await store.ExistsAsync("forever"));
    }

    [Fact]
    public void Store_RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InMemoryIdempotencyStore(new InMemoryIdempotencyStoreOptions { MaxEntries = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InMemoryIdempotencyStore(new InMemoryIdempotencyStoreOptions { DefaultExpiration = TimeSpan.Zero }));
    }

    [Fact]
    public async Task Store_StaysBoundedUnderConcurrentWrites()
    {
        var store = new InMemoryIdempotencyStore(new InMemoryIdempotencyStoreOptions { MaxEntries = 50 });

        await Task.WhenAll(Enumerable.Range(0, 1000).Select(i => Task.Run(() => store.StoreAsync(Entry("k" + i)))));

        var present = 0;
        for (var i = 0; i < 1000; i++)
            if (await store.ExistsAsync("k" + i)) present++;
        Assert.Equal(50, present);
    }

    // ---- AUD-C-08: converter keeps ErrorType -------------------------------------------------------

    [Fact]
    public void Converter_KeepsErrorTypeAndValidationErrors()
    {
        var serializer = new JsonIdempotencySerializer();
        var errors = new Dictionary<string, List<string>> { ["name"] = new List<string> { "required" } };
        var original = Result<int>.Fail(errors, ErrorType.Conflict);

        var copy = serializer.Deserialize<Result<int>>(serializer.Serialize(original));

        Assert.True(copy.IsFailure);
        Assert.Equal(ErrorType.Conflict, copy.ErrorType);
        Assert.Equal("required", copy.ValidationErrors!["name"][0]);
    }

    [Fact]
    public void Converter_KeepsErrorTypeOfNonGenericResult()
    {
        var serializer = new JsonIdempotencySerializer();

        var copy = serializer.Deserialize<Result>(serializer.Serialize(Result.Fail("gone", ErrorType.NotFound)));

        Assert.True(copy.IsFailure);
        Assert.Equal(ErrorType.NotFound, copy.ErrorType);
        Assert.Equal("gone", copy.Error);
    }
}
