using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Vali_Mediator_Idempotency.Core.Abstractions;
using Vali_Mediator_Idempotency.Core.Interfaces;
using Vali_Mediator_Idempotency.Core.Models;
using Vali_Mediator_Idempotency.Core.Options;
using Vali_Mediator_Idempotency.Extension;
using Xunit;

namespace Vali_Mediator_Idempotency.Tests;

public class IdempotencyConfigurationTests
{
    private sealed class CfgReq : IRequest<Result<string>>, IIdempotent
    {
        public string IdempotencyKey { get; init; } = string.Empty;
        public TimeSpan? Expiration { get; init; }
        public string Payload { get; init; } = string.Empty;
    }

    private sealed class CfgHandler : IRequestHandler<CfgReq, Result<string>>
    {
        public static int Calls;
        public Task<Result<string>> Handle(CfgReq request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Result<string>.Ok("v:" + request.Payload));
        }
    }

    private static ServiceProvider Build(
        Action<IdempotencyOptions>? options = null,
        Action<InMemoryIdempotencyStoreOptions>? storeOptions = null)
    {
        var services = new ServiceCollection();
        if (options is not null) services.AddIdempotencyOptions(options);
        services.AddValiMediator(config =>
        {
            config.RegisterServicesFromAssemblyContaining<IdempotencyConfigurationTests>();
            config.AddIdempotencyBehavior();
        });
        if (storeOptions is not null) services.AddInMemoryIdempotencyStore(storeOptions);
        else services.AddInMemoryIdempotencyStore();
        return services.BuildServiceProvider();
    }

    public static IEnumerable<object[]> InvalidPositiveInts()
    {
        yield return new object[] { 0 };
        yield return new object[] { -1 };
        yield return new object[] { int.MinValue };
    }

    public static IEnumerable<object[]> ValidPositiveInts()
    {
        yield return new object[] { 1 };
        yield return new object[] { 256 };
        yield return new object[] { int.MaxValue };
    }

    [Theory]
    [MemberData(nameof(InvalidPositiveInts))]
    public void MaxKeyLength_RejectsNonPositive_AtConfigurationTime(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new IdempotencyOptions { MaxKeyLength = value });
        Assert.Equal("MaxKeyLength", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(o => o.MaxKeyLength = value));
    }

    [Theory]
    [MemberData(nameof(ValidPositiveInts))]
    public void MaxKeyLength_AcceptsPositive(int value)
        => Assert.Equal(value, new IdempotencyOptions { MaxKeyLength = value }.MaxKeyLength);

    [Theory]
    [MemberData(nameof(InvalidPositiveInts))]
    public void StoreMaxEntries_RejectsNonPositive_AtConfigurationTime(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryIdempotencyStoreOptions { MaxEntries = value });
        Assert.Equal("MaxEntries", ex.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(storeOptions: o => o.MaxEntries = value));
    }

    [Theory]
    [MemberData(nameof(ValidPositiveInts))]
    public void StoreMaxEntries_AcceptsPositive(int value)
        => Assert.Equal(value, new InMemoryIdempotencyStoreOptions { MaxEntries = value }.MaxEntries);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StoreDefaultExpiration_RejectsZeroAndNegative(int seconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new InMemoryIdempotencyStoreOptions { DefaultExpiration = TimeSpan.FromSeconds(seconds) });
        Assert.Equal("DefaultExpiration", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(86_400L * 365)]
    public void StoreDefaultExpiration_AcceptsNullAndPositive(long? seconds)
    {
        TimeSpan? value = seconds is null ? null : TimeSpan.FromSeconds(seconds.Value);
        Assert.Equal(value, new InMemoryIdempotencyStoreOptions { DefaultExpiration = value }.DefaultExpiration);
    }

    [Fact]
    public void Defaults_AreUnchanged()
    {
        var o = new IdempotencyOptions();
        var s = new InMemoryIdempotencyStoreOptions();
        Assert.Equal(256, o.MaxKeyLength);
        Assert.True(o.VerifyRequestFingerprint);
        Assert.Equal(10_000, s.MaxEntries);
        Assert.Equal(TimeSpan.FromHours(24), s.DefaultExpiration);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(50)]
    public async Task StoreMaxEntries_ConfiguredThroughDi_BoundsTheStore(int maxEntries)
    {
        await using var provider = Build(storeOptions: o => o.MaxEntries = maxEntries);
        var store = provider.GetRequiredService<IIdempotencyStore>();

        for (var i = 0; i < maxEntries + 5; i++)
            await store.StoreAsync(new IdempotencyEntry
            {
                Key = "k" + i,
                SerializedResponse = new byte[] { 1 },
                ResponseTypeName = "System.String",
                CreatedAt = DateTimeOffset.UtcNow
            });

        var alive = 0;
        for (var i = 0; i < maxEntries + 5; i++)
            if (await store.ExistsAsync("k" + i)) alive++;

        Assert.Equal(maxEntries, alive);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(8, false)]
    [InlineData(256, false)]
    public async Task MaxKeyLength_ConfiguredThroughDi_RejectsLongKeys(int maxKeyLength, bool rejectsEightChars)
    {
        await using var provider = Build(o => o.MaxKeyLength = maxKeyLength);
        var mediator = provider.GetRequiredService<IValiMediator>();
        var request = new CfgReq { IdempotencyKey = "12345678", Payload = "x" };

        if (rejectsEightChars)
            await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(request));
        else
            Assert.True((await mediator.Send(request)).IsSuccess);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerifyRequestFingerprint_ConfiguredThroughDi_ControlsConflicts(bool verify)
    {
        await using var provider = Build(o => o.VerifyRequestFingerprint = verify);
        var mediator = provider.GetRequiredService<IValiMediator>();
        var key = "fp-" + verify;

        var first = await mediator.Send(new CfgReq { IdempotencyKey = key, Payload = "one" });
        var second = await mediator.Send(new CfgReq { IdempotencyKey = key, Payload = "two" });

        Assert.True(first.IsSuccess);
        if (verify)
            Assert.Equal(ErrorType.Conflict, second.ErrorType);
        else
        {
            Assert.True(second.IsSuccess);
            Assert.Equal("v:one", second.Value);
        }
    }
}
