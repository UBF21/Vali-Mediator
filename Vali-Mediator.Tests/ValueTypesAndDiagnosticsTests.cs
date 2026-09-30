using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Exceptions;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Xunit;

namespace Vali_Mediator.Tests;

public class ValueTypesAndDiagnosticsTests
{
    // ---- Unit ----

    [Fact]
    public void Unit_IsAValueTypeWithASingleValue()
    {
        var other = new Unit();

        Assert.True(Unit.Value.Equals(other));
        Assert.True(Unit.Value.Equals((object)other));
        Assert.False(Unit.Value.Equals("()"));
        Assert.Equal(0, Unit.Value.GetHashCode());
        Assert.Equal("()", Unit.Value.ToString());
    }

    // ---- exceptions ----

    private sealed class WrappedException : ValiMediatorException
    {
        public WrappedException(string message, Exception inner) : base(message, inner) { }
    }

    [Fact]
    public void ValiMediatorException_KeepsTheInnerException()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new WrappedException("outer", inner);

        Assert.Equal("outer", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.IsAssignableFrom<Exception>(exception);
    }

    private record Unregistered : IRequest<int>;

    [Fact]
    public async Task HandlerNotFoundException_ExposesTheRequestType()
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<HandlerNotFoundException>(() =>
            provider.GetRequiredService<IValiMediator>().Send(new Unregistered()));

        Assert.Equal(typeof(Unregistered), exception.RequestType);
        Assert.Contains(nameof(Unregistered), exception.Message);
        Assert.IsAssignableFrom<ValiMediatorException>(exception);
    }

    // ---- Result<T> ----

    [Fact]
    public void ResultOfT_ToString_DescribesEveryShape()
    {
        var validation = Result<int>.Fail(new Dictionary<string, List<string>>
        {
            ["name"] = new() { "required" },
            ["age"] = new() { "too low" },
        });

        Assert.Equal("Ok(5)", Result<int>.Ok(5).ToString());
        Assert.Equal("ValidationFail(2 properties)", validation.ToString());
        Assert.Equal("Fail(NotFound: missing)", Result<int>.Fail("missing", ErrorType.NotFound).ToString());
    }

    // ---- Result (void) ----

    [Fact]
    public void Result_OnFailure_RunsOnlyForFailures()
    {
        string? seenError = null;
        ErrorType? seenType = null;

        var failed = Result.Fail("bad", ErrorType.Conflict).OnFailure((e, t) => { seenError = e; seenType = t; });
        var ok = Result.Ok().OnFailure((_, _) => throw new InvalidOperationException("must not run"));

        Assert.Equal("bad", seenError);
        Assert.Equal(ErrorType.Conflict, seenType);
        Assert.True(failed.IsFailure);
        Assert.True(ok.IsSuccess);
        Assert.Throws<ArgumentNullException>(() => Result.Ok().OnFailure(null!));
    }

    [Fact]
    public void Result_Equality_HashCodeAndToString()
    {
        var a = Result.Fail("x", ErrorType.NotFound);
        var b = Result.Fail("x", ErrorType.NotFound);

        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals(Result.Ok()));
        Assert.False(a.Equals("x"));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal("Ok()", Result.Ok().ToString());
        Assert.Equal("Fail(NotFound: x)", a.ToString());
    }

    [Fact]
    public void Result_Tap_RunsOnlyForSuccess()
    {
        var calls = 0;

        Result.Ok().Tap(() => calls++);
        Result.Fail("x").Tap(() => calls++);

        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => Result.Ok().Tap(null!));
    }

    // ---- in-memory dead letter queue ----

    private static DeadLetterEntry Entry(string name) => new()
    {
        NotificationTypeName = name,
        HandlerTypeName = "H",
        Exception = new InvalidOperationException(name),
        Notification = new object(),
    };

    [Fact]
    public async Task InMemoryDeadLetterQueue_ClearRemovesEveryEntry()
    {
        var queue = new InMemoryDeadLetterQueue();
        await queue.EnqueueAsync(Entry("a"));
        await queue.EnqueueAsync(Entry("b"));
        Assert.Equal(2, queue.Count);

        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.Empty(queue.GetEntries());
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_DiscardsTheOldestBeyondItsCapacity()
    {
        var queue = new InMemoryDeadLetterQueue(maxEntries: 2);

        await queue.EnqueueAsync(Entry("a"));
        await queue.EnqueueAsync(Entry("b"));
        await queue.EnqueueAsync(Entry("c"));

        Assert.Equal(new[] { "b", "c" }, queue.GetEntries().Select(e => e.NotificationTypeName));
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_ValidatesItsArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryDeadLetterQueue(0));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new InMemoryDeadLetterQueue().EnqueueAsync(null!));
    }

    // ---- assembly scanning ----

    private sealed class HalfLoadedAssembly : Assembly
    {
        public override Type[] GetTypes() =>
            throw new ReflectionTypeLoadException(
                new Type?[] { typeof(LoadedHandler), null },
                new Exception[] { new FileNotFoundException("missing dependency") });
    }

    private record LoadedRequest : IRequest<string>;

    private sealed class LoadedHandler : IRequestHandler<LoadedRequest, string>
    {
        public Task<string> Handle(LoadedRequest request, CancellationToken cancellationToken) =>
            Task.FromResult("loaded");
    }

    [Fact]
    public async Task AddValiMediator_KeepsTheTypesThatLoadedWhenAnAssemblyIsPartiallyLoadable()
    {
        var services = new ServiceCollection();
        services.AddValiMediator(c => c.RegisterServicesFromAssembly(new HalfLoadedAssembly()));
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IValiMediator>().Send(new LoadedRequest());

        Assert.Equal("loaded", result);
    }
}
