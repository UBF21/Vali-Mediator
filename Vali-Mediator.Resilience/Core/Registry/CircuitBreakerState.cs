using System.Collections.Concurrent;
using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;

namespace Vali_Mediator_Resilience.Core.Registry;

/// <summary>
/// Thread-safe state machine for a single circuit breaker keyed by <see cref="CircuitBreakerOptions.CircuitKey"/>.
/// </summary>
public sealed class CircuitBreakerState
{
    private readonly CircuitBreakerOptions _options;

    // Sliding-window entries: timestamp of the call result
    private readonly ConcurrentQueue<(DateTimeOffset Timestamp, bool IsFailure)> _window
        = new ConcurrentQueue<(DateTimeOffset, bool)>();

    private volatile int _state = (int)CircuitState.Closed;
    private DateTimeOffset _openedAt;
    private int _halfOpenAttempts;

    // Protects transitions; reads are lock-free via volatile/Interlocked
    private readonly object _lock = new object();

    public CircuitState State => (CircuitState)_state;

    public CircuitBreakerState(CircuitBreakerOptions options)
    {
        _options = options;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> if a call should be allowed through;
    /// <c>false</c> if the circuit is open (caller should throw CircuitOpenException).
    /// </summary>
    public bool CanExecute()
    {
        var current = State;

        if (current == CircuitState.Closed) return true;

        if (current == CircuitState.Open)
        {
            // Try transition to HalfOpen after BreakDuration
            if (DateTimeOffset.UtcNow - _openedAt >= _options.BreakDuration)
            {
                lock (_lock)
                {
                    if (State == CircuitState.Open)
                    {
                        TransitionTo(CircuitState.HalfOpen);
                        return true; // first probe
                    }
                }
            }
            return false;
        }

        // HalfOpen: allow up to HalfOpenMaxAttempts probes
        if (current == CircuitState.HalfOpen)
        {
            lock (_lock)
            {
                if (State == CircuitState.HalfOpen && _halfOpenAttempts < _options.HalfOpenMaxAttempts)
                {
                    _halfOpenAttempts++;
                    return true;
                }
            }
            return false;
        }

        return false;
    }

    /// <summary>Records a successful call outcome and potentially closes the circuit.</summary>
    public void RecordSuccess()
    {
        AddToWindow(isFailure: false);

        if (State == CircuitState.HalfOpen)
        {
            lock (_lock)
            {
                if (State == CircuitState.HalfOpen)
                    TransitionTo(CircuitState.Closed);
            }
        }
    }

    /// <summary>Records a failed call outcome and potentially opens the circuit.</summary>
    public void RecordFailure()
    {
        AddToWindow(isFailure: true);

        if (State == CircuitState.HalfOpen)
        {
            lock (_lock)
            {
                if (State == CircuitState.HalfOpen)
                {
                    _openedAt = DateTimeOffset.UtcNow;
                    TransitionTo(CircuitState.Open);
                }
            }
            return;
        }

        if (State == CircuitState.Closed && ShouldTrip())
        {
            lock (_lock)
            {
                if (State == CircuitState.Closed && ShouldTrip())
                {
                    _openedAt = DateTimeOffset.UtcNow;
                    TransitionTo(CircuitState.Open);
                }
            }
        }
    }

    /// <summary>Manually resets the circuit to <see cref="CircuitState.Closed"/>.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            while (_window.TryDequeue(out _)) { }
            _halfOpenAttempts = 0;
            TransitionTo(CircuitState.Closed);
        }
    }

    /// <summary>Returns the time remaining before the circuit tries to recover. Zero if already HalfOpen/Closed.</summary>
    public TimeSpan RetryAfter()
    {
        if (State != CircuitState.Open) return TimeSpan.Zero;
        var remaining = _options.BreakDuration - (DateTimeOffset.UtcNow - _openedAt);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private void AddToWindow(bool isFailure)
    {
        var now = DateTimeOffset.UtcNow;
        _window.Enqueue((now, isFailure));
        PurgeOldEntries(now);
    }

    private void PurgeOldEntries(DateTimeOffset now)
    {
        var cutoff = now - _options.SamplingDuration;
        while (_window.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            _window.TryDequeue(out _);
    }

    private bool ShouldTrip()
    {
        var now = DateTimeOffset.UtcNow;
        PurgeOldEntries(now);

        var snapshot = _window.ToArray(); // snapshot for consistent read
        int total = snapshot.Length;
        int failures = 0;
        foreach (var entry in snapshot)
            if (entry.IsFailure) failures++;

        // Rate-based threshold
        if (_options.FailureRateThreshold > 0)
        {
            if (total < _options.MinimumThroughput) return false;
            return (double)failures / total >= _options.FailureRateThreshold;
        }

        // Absolute threshold
        return failures >= _options.FailureThreshold;
    }

    private void TransitionTo(CircuitState newState)
    {
        _state = (int)newState;
        if (newState == CircuitState.HalfOpen) _halfOpenAttempts = 0;
        if (newState == CircuitState.Closed) _halfOpenAttempts = 0;
    }
}
