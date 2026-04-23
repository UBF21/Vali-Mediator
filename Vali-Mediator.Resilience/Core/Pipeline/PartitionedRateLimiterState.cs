using System.Collections.Concurrent;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Pipeline;

internal sealed class PartitionedRateLimiterState : IDisposable
{
    private readonly RateLimiterOptions _options;
    private readonly ConcurrentDictionary<string, RateLimiterState> _states = new ConcurrentDictionary<string, RateLimiterState>();
    private bool _disposed;

    internal PartitionedRateLimiterState(RateLimiterOptions options)
    {
        _options = options;
    }

    internal RateLimiterState GetOrCreate(string partitionKey)
        => _states.GetOrAdd(partitionKey, _ => new RateLimiterState(_options));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var state in _states.Values)
            state.Dispose();
        _states.Clear();
    }
}
