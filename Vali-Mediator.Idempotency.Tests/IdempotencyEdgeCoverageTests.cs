using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Vali_Mediator_Idempotency.Core.Abstractions;
using Vali_Mediator_Idempotency.Core.Interfaces;
using Vali_Mediator_Idempotency.Core.Models;
using Vali_Mediator_Idempotency.Core.Options;
using Vali_Mediator_Idempotency.Core.Serialization;
using Vali_Mediator_Idempotency.Core.Store;
using Vali_Mediator_Idempotency.Extension;
using Vali_Mediator_Idempotency.Pipeline;
using Xunit;

namespace Vali_Mediator_Idempotency.Tests;

internal sealed class EdgeReq : IRequest<string>, IIdempotent
{
    public string IdempotencyKey { get; init; } = "edge";
    public TimeSpan? Expiration => TimeSpan.FromMinutes(5);
    public string Payload { get; init; } = "a";
}

internal sealed class EdgeResultReq : IRequest<Result>, IIdempotent
{
    public string IdempotencyKey { get; init; } = "edge-result";
    public TimeSpan? Expiration => TimeSpan.FromMinutes(5);
    public string Payload { get; init; } = "a";
}

internal sealed class Clock : TimeProvider
{
    private DateTimeOffset _now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

// A store that knows nothing about reservations (the interface defaults apply).
internal sealed class PlainStore : IIdempotencyStore
{
    private readonly Dictionary<string, IdempotencyEntry> _map = new Dictionary<string, IdempotencyEntry>();

    public Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_map.TryGetValue(key, out var entry) ? entry : null);

    public Task StoreAsync(IdempotencyEntry entry, CancellationToken ct = default)
    {
        _map[entry.Key] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _map.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(_map.ContainsKey(key));
}

// Another instance answers the request right before it releases the reservation this instance then obtains.
internal sealed class LateWinnerStore : IIdempotencyStore
{
    private readonly InMemoryIdempotencyStore _inner = new InMemoryIdempotencyStore();
    private readonly Func<string, IdempotencyEntry> _late;

    public int Released;

    public LateWinnerStore(Func<string, IdempotencyEntry> late) => _late = late;

    public bool SupportsReservation => true;

    public Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct = default) => _inner.FindAsync(key, ct);

    public Task StoreAsync(IdempotencyEntry entry, CancellationToken ct = default) => _inner.StoreAsync(entry, ct);

    public Task RemoveAsync(string key, CancellationToken ct = default) => _inner.RemoveAsync(key, ct);

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => _inner.ExistsAsync(key, ct);

    public async Task<string?> TryReserveAsync(string key, TimeSpan lease, CancellationToken ct = default)
    {
        await _inner.StoreAsync(_late(key), ct);
        return await _inner.TryReserveAsync(key, lease, ct);
    }

    public Task ReleaseReservationAsync(string key, string token, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Released);
        return _inner.ReleaseReservationAsync(key, token, ct);
    }
}

// Cannot fingerprint the request: serializing it throws the configured exception; everything else works.
internal sealed class RequestHostileSerializer : IIdempotencySerializer
{
    private readonly JsonIdempotencySerializer _json = new JsonIdempotencySerializer();
    private readonly Exception _failure;

    public RequestHostileSerializer(Exception failure) => _failure = failure;

    public byte[] Serialize<T>(T value) => value is EdgeReq ? throw _failure : _json.Serialize(value);

    public T? Deserialize<T>(byte[] data) => _json.Deserialize<T>(data);
}

/// <summary>Registration extensions, argument guards, serializer/converter edges and the store sweep.</summary>
public sealed class IdempotencyEdgeCoverageTests
{
    private static IdempotencyBehavior<EdgeReq, string> Behavior(IIdempotencyStore store, IIdempotencySerializer? serializer = null)
        => new IdempotencyBehavior<EdgeReq, string>(store, serializer ?? new JsonIdempotencySerializer());

    // -------------------------------------------------------------------------------------------------------------
    // Entry
    // -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Entry_IsExpired_FollowsTheExpiryInstant()
    {
        Assert.False(new IdempotencyEntry().IsExpired);
        Assert.False(new IdempotencyEntry { ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }.IsExpired);
        Assert.True(new IdempotencyEntry { ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) }.IsExpired);
    }

    // -------------------------------------------------------------------------------------------------------------
    // Serializer + converter
    // -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Serializer_NullOrEmptyPayload_DeserializesToDefault()
    {
        var serializer = new JsonIdempotencySerializer();

        Assert.Null(serializer.Deserialize<string>(null!));
        Assert.Null(serializer.Deserialize<string>(Array.Empty<byte>()));
        Assert.Equal(default, serializer.Deserialize<int>(Array.Empty<byte>()));
    }

    [Theory]
    [InlineData(ErrorType.None)]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.Failure)]
    public void Converter_RoundTripsEveryErrorType_ForResultAndResultOfT(ErrorType errorType)
    {
        var serializer = new JsonIdempotencySerializer();

        var plain = serializer.Deserialize<Result>(serializer.Serialize(Result.Fail("boom", errorType)));
        var typed = serializer.Deserialize<Result<int>>(serializer.Serialize(Result<int>.Fail("boom", errorType)));

        Assert.True(plain.IsFailure);
        Assert.Equal("boom", plain.Error);
        Assert.Equal(errorType, plain.ErrorType);
        Assert.True(typed.IsFailure);
        Assert.Equal("boom", typed.Error);
        Assert.Equal(errorType, typed.ErrorType);
    }

    [Fact]
    public void Converter_FailureWithoutAMessage_ReadsBackWithAnEmptyError()
    {
        var serializer = new JsonIdempotencySerializer();

        var plain = serializer.Deserialize<Result>(System.Text.Encoding.UTF8.GetBytes("{\"isSuccess\":false,\"errorType\":3}"));
        var typed = serializer.Deserialize<Result<string>>(System.Text.Encoding.UTF8.GetBytes("{\"isSuccess\":false,\"errorType\":2}"));

        Assert.True(plain.IsFailure);
        Assert.Equal(string.Empty, plain.Error);
        Assert.Equal(ErrorType.Conflict, plain.ErrorType);
        Assert.True(typed.IsFailure);
        Assert.Equal(string.Empty, typed.Error);
        Assert.Equal(ErrorType.NotFound, typed.ErrorType);
    }

    [Fact]
    public void Converter_SuccessfulResults_AndNestedValues_RoundTrip()
    {
        var serializer = new JsonIdempotencySerializer();
        var nested = new Dictionary<string, List<int>> { ["a"] = new List<int> { 1, 2 }, ["b"] = new List<int>() };

        var ok = serializer.Deserialize<Result>(serializer.Serialize(Result.Ok()));
        var value = serializer.Deserialize<Result<Dictionary<string, List<int>>>>(
            serializer.Serialize(Result<Dictionary<string, List<int>>>.Ok(nested)));

        Assert.True(ok.IsSuccess);
        Assert.True(value.IsSuccess);
        Assert.Equal(new[] { 1, 2 }, value.Value!["a"]);
        Assert.Empty(value.Value["b"]);
    }

    [Fact]
    public void Converter_CanConvertOnlyResultTypes()
    {
        var factory = new ResultJsonConverterFactory();

        Assert.True(factory.CanConvert(typeof(Result)));
        Assert.True(factory.CanConvert(typeof(Result<int>)));
        Assert.False(factory.CanConvert(typeof(string)));
        Assert.False(factory.CanConvert(typeof(List<int>)));
    }

    // -------------------------------------------------------------------------------------------------------------
    // Store
    // -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Store_NullEntry_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => new InMemoryIdempotencyStore().StoreAsync(null!));

    [Fact]
    public async Task Store_Sweep_DropsExpiredEntriesAndStaleReservations_KeepsTheLiveOnes()
    {
        var clock = new Clock();
        var store = new InMemoryIdempotencyStore(new InMemoryIdempotencyStoreOptions { MaxEntries = 1000 }, clock);
        var now = clock.GetUtcNow();

        var staleToken = await store.TryReserveAsync("stale", TimeSpan.FromSeconds(10));
        var liveToken = await store.TryReserveAsync("live", TimeSpan.FromDays(1));
        await store.StoreAsync(new IdempotencyEntry { Key = "old", CreatedAt = now, ExpiresAt = now.AddSeconds(1) });
        await store.StoreAsync(new IdempotencyEntry { Key = "fresh", CreatedAt = now, ExpiresAt = now.AddDays(2) });
        Assert.NotNull(staleToken);
        Assert.NotNull(liveToken);

        clock.Advance(TimeSpan.FromMinutes(5));

        // The sweep runs every max(100, MaxEntries / 10) writes.
        for (var i = 0; i < 100; i++)
            await store.StoreAsync(new IdempotencyEntry { Key = "filler-" + i, CreatedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddDays(2) });

        Assert.False(await store.ExistsAsync("old"));
        Assert.True(await store.ExistsAsync("fresh"));
        Assert.NotNull(await store.TryReserveAsync("stale", TimeSpan.FromSeconds(10)));
        Assert.Null(await store.TryReserveAsync("live", TimeSpan.FromSeconds(10)));
    }

    // -------------------------------------------------------------------------------------------------------------
    // Behavior
    // -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Behavior_NullCollaborators_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => new IdempotencyBehavior<EdgeReq, string>(null!, new JsonIdempotencySerializer()));
        Assert.Throws<ArgumentNullException>(
            () => new IdempotencyBehavior<EdgeReq, string>(new InMemoryIdempotencyStore(), null!));
    }

    [Fact]
    public async Task StoreWithoutReservation_StillReplaysTheStoredAnswer()
    {
        var behavior = Behavior(new PlainStore());
        var runs = 0;
        Func<CancellationToken, Task<string>> next = _ =>
        {
            Interlocked.Increment(ref runs);
            return Task.FromResult("answer");
        };

        var first = await behavior.Handle(new EdgeReq(), next, CancellationToken.None);
        var second = await behavior.Handle(new EdgeReq(), next, CancellationToken.None);

        Assert.Equal("answer", first);
        Assert.Equal("answer", second);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task StoreWithoutReservation_HandlerFailure_PropagatesAndStoresNothing()
    {
        var store = new PlainStore();
        var behavior = Behavior(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new EdgeReq(),
            _ => throw new InvalidOperationException("boom"),
            CancellationToken.None));

        var retry = await behavior.Handle(new EdgeReq(), _ => Task.FromResult("recovered"), CancellationToken.None);
        Assert.Equal("recovered", retry);
    }

    [Fact]
    public async Task ReservationObtainedAfterAnotherInstanceAnswered_ReplaysTheLateAnswer_AndReleases()
    {
        var serializer = new JsonIdempotencySerializer();
        var store = new LateWinnerStore(key => new IdempotencyEntry
        {
            Key = key,
            SerializedResponse = serializer.Serialize("late"),
            ResponseTypeName = typeof(string).AssemblyQualifiedName!,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        });
        var runs = 0;

        var result = await Behavior(store, serializer).Handle(
            new EdgeReq(),
            _ =>
            {
                Interlocked.Increment(ref runs);
                return Task.FromResult("handler");
            },
            CancellationToken.None);

        Assert.Equal("late", result);
        Assert.Equal(0, runs);
        Assert.Equal(1, store.Released);
    }

    [Theory]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task RequestThatCannotBeSerialized_IsNotFingerprinted_AndStillRunsOnce(Type failure)
    {
        var serializer = new RequestHostileSerializer((Exception)Activator.CreateInstance(failure)!);
        var behavior = Behavior(new InMemoryIdempotencyStore(), serializer);
        var runs = 0;
        Func<CancellationToken, Task<string>> next = _ =>
        {
            Interlocked.Increment(ref runs);
            return Task.FromResult("answer");
        };

        var first = await behavior.Handle(new EdgeReq(), next, CancellationToken.None);
        var second = await behavior.Handle(new EdgeReq { Payload = "different" }, next, CancellationToken.None);

        Assert.Equal("answer", first);
        Assert.Equal("answer", second);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task UnexpectedSerializerFailure_WhileFingerprinting_Propagates()
    {
        var serializer = new RequestHostileSerializer(new ArgumentException("not a serialization problem"));

        await Assert.ThrowsAsync<ArgumentException>(() => Behavior(new InMemoryIdempotencyStore(), serializer)
            .Handle(new EdgeReq(), _ => Task.FromResult("never"), CancellationToken.None));
    }

    [Fact]
    public async Task SamePayloadDifferentKeysScope_DoNotCollide_ForNonGenericResult()
    {
        var behavior = new IdempotencyBehavior<EdgeResultReq, Result>(new InMemoryIdempotencyStore(), new JsonIdempotencySerializer());

        var first = await behavior.Handle(new EdgeResultReq(), _ => Task.FromResult(Result.Ok()), CancellationToken.None);
        var conflicting = await behavior.Handle(new EdgeResultReq { Payload = "b" }, _ => Task.FromResult(Result.Ok()), CancellationToken.None);
        var replay = await behavior.Handle(new EdgeResultReq(), _ => Task.FromResult(Result.Fail("must not run")), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(conflicting.IsFailure);
        Assert.Equal(ErrorType.Conflict, conflicting.ErrorType);
        Assert.Contains("different request payload", conflicting.Error);
        Assert.True(replay.IsSuccess);
    }

    // -------------------------------------------------------------------------------------------------------------
    // Registration
    // -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Extensions_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ValiMediatorIdempotencyExtension.AddIdempotencyBehavior(null!));
        Assert.Throws<ArgumentNullException>(() => ValiMediatorIdempotencyExtension.AddInMemoryIdempotencyStore(null!));
        Assert.Throws<ArgumentNullException>(
            () => ValiMediatorIdempotencyExtension.AddInMemoryIdempotencyStore(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddInMemoryIdempotencyStore((Action<InMemoryIdempotencyStoreOptions>)null!));
        Assert.Throws<ArgumentNullException>(
            () => ValiMediatorIdempotencyExtension.AddIdempotencyOptions(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddIdempotencyOptions(null!));
        Assert.Throws<ArgumentNullException>(() => ValiMediatorIdempotencyExtension.AddIdempotencyStore<PlainStore>(null!));
        Assert.Throws<ArgumentNullException>(() => ValiMediatorIdempotencyExtension.AddIdempotencySerializer<JsonIdempotencySerializer>(null!));
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void AddIdempotencyStore_AndSerializer_RegisterTheCustomTypesWithTheRequestedLifetime(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();

        services.AddIdempotencyStore<PlainStore>(lifetime);
        services.AddIdempotencySerializer<JsonIdempotencySerializer>(lifetime);

        Assert.Collection(
            services,
            d =>
            {
                Assert.Equal(typeof(IIdempotencyStore), d.ServiceType);
                Assert.Equal(typeof(PlainStore), d.ImplementationType);
                Assert.Equal(lifetime, d.Lifetime);
            },
            d =>
            {
                Assert.Equal(typeof(IIdempotencySerializer), d.ServiceType);
                Assert.Equal(typeof(JsonIdempotencySerializer), d.ImplementationType);
                Assert.Equal(lifetime, d.Lifetime);
            });
    }

    [Fact]
    public void AddIdempotencyStore_DefaultsToSingleton_AndResolves()
    {
        var services = new ServiceCollection();
        services.AddIdempotencyStore<PlainStore>();
        services.AddIdempotencySerializer<JsonIdempotencySerializer>();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<PlainStore>(provider.GetRequiredService<IIdempotencyStore>());
        Assert.Same(provider.GetRequiredService<IIdempotencyStore>(), provider.GetRequiredService<IIdempotencyStore>());
        Assert.IsType<JsonIdempotencySerializer>(provider.GetRequiredService<IIdempotencySerializer>());
    }
}
