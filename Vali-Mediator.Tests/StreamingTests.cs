using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Exceptions;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Core.General.Mediator;
using Vali_Mediator.Core.Streaming;
using Xunit;

namespace Vali_Mediator.Tests;

public class StreamingTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddValiMediator(_ => { });
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // -------------------------------------------------------------------------
    // Inline types
    // -------------------------------------------------------------------------

    private record NumbersRequest(int Count) : IStreamRequest<int>;

    private class NumbersHandler : IStreamRequestHandler<NumbersRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(NumbersRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (int i = 1; i <= request.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return i;
                await Task.Yield();
            }
        }
    }

    private record UnknownStreamRequest : IStreamRequest<int>;

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateStream_YieldsAllElements()
    {
        await using var sp = BuildProvider(s =>
            s.AddTransient<IStreamRequestHandler<NumbersRequest, int>, NumbersHandler>());

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        var results = new List<int>();
        await foreach (var item in mediator.CreateStream(new NumbersRequest(5)))
        {
            results.Add(item);
        }

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, results);
    }

    [Fact]
    public async Task CreateStream_WithCancellation_StopsEnumeration()
    {
        await using var sp = BuildProvider(s =>
            s.AddTransient<IStreamRequestHandler<NumbersRequest, int>, NumbersHandler>());

        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        using var cts = new CancellationTokenSource();
        var results = new List<int>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in mediator.CreateStream(new NumbersRequest(100), cts.Token))
            {
                results.Add(item);
                if (results.Count >= 3)
                    cts.Cancel();
            }
        });

        // At least 3 items collected before cancellation
        Assert.True(results.Count >= 3);
        Assert.True(results.Count < 100);
    }

    [Fact]
    public void CreateStream_WithoutHandler_ThrowsHandlerNotFoundException()
    {
        using var sp = BuildProvider();
        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IValiMediator>();

        Assert.Throws<HandlerNotFoundException>(
            () => mediator.CreateStream(new UnknownStreamRequest()));
    }
}
