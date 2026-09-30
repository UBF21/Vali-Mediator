using Vali_Mediator_Resilience.Core.Enums;
using Vali_Mediator_Resilience.Core.Options;
using Vali_Mediator_Resilience.Core.Pipeline;

namespace Vali_Mediator_Resilience.Core.Registry;

/// <summary>
/// Thread-safe state machine for a single circuit breaker keyed by <see cref="CircuitBreakerOptions.CircuitKey"/>.
/// </summary>
/// <remarks>
/// Every state transition starts a new <em>epoch</em>. A call remembers the epoch it was admitted in, and its
/// verdict is ignored when the circuit has moved on meanwhile, so a slow call that started while the circuit was
/// closed cannot close or re-open it from a later state.
/// </remarks>
public sealed class CircuitBreakerState
{
    private readonly CircuitBreakerOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    // All members below are guarded by _lock, except the volatile mirrors used by lock-free reads.
    private readonly object _lock = new object();
    private readonly Queue<(long Ticks, bool IsFailure)> _window = new Queue<(long, bool)>();
    private int _windowFailures;
    private CircuitState _current = CircuitState.Closed;
    private long _openedAtTicks;
    private long _lastProbeTicks;
    private int _halfOpenAttempts;

    private int _stateMirror = (int)CircuitState.Closed;
    private int _epochMirror;

    /// <summary>The current state of the circuit.</summary>
    public CircuitState State => (CircuitState)Volatile.Read(ref _stateMirror);

    internal int Epoch => Volatile.Read(ref _epochMirror);

    /// <summary>Creates a breaker driven by <paramref name="options"/>.</summary>
    public CircuitBreakerState(CircuitBreakerOptions options) : this(options, () => MonotonicClock.Now)
    {
    }

    internal CircuitBreakerState(CircuitBreakerOptions options, Func<DateTimeOffset> clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> if a call should be allowed through;
    /// <c>false</c> if the circuit is open (caller should throw CircuitOpenException).
    /// </summary>
    public bool CanExecute() => TryEnter(out _, out _);

    /// <summary>Records a successful call outcome and potentially closes the circuit.</summary>
    public void RecordSuccess() => RecordSuccessAndCheckClosed(Epoch);

    /// <summary>Records a failed call outcome and potentially opens the circuit.</summary>
    public void RecordFailure() => RecordFailureAndCheckOpened(Epoch);

    /// <summary>Manually resets the circuit to <see cref="CircuitState.Closed"/>.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            TransitionTo(CircuitState.Closed);
        }
    }

    /// <summary>Returns the time remaining before the circuit tries to recover. Zero if already HalfOpen/Closed.</summary>
    public TimeSpan RetryAfter()
    {
        if (State != CircuitState.Open) return TimeSpan.Zero;
        var elapsed = TimeSpan.FromTicks(Now() - Interlocked.Read(ref _openedAtTicks));
        var remaining = _options.BreakDuration - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    // -----------------------------------------------------------------------
    // Pipeline API (epoch-aware)
    // -----------------------------------------------------------------------

    /// <summary>Admits a call. <paramref name="epoch"/> must be handed back with the call's verdict.</summary>
    internal bool TryEnter(out int epoch, out bool enteredHalfOpen)
    {
        enteredHalfOpen = false;

        // Fast path. Epoch is read before the state so a racing transition can only make the epoch stale
        // (verdict ignored), never newer than the state that admitted the call.
        epoch = Epoch;
        if (State == CircuitState.Closed) return true;

        lock (_lock)
        {
            epoch = _epochMirror;
            long now = Now();

            switch (_current)
            {
                case CircuitState.Closed:
                    return true;

                case CircuitState.Open:
                    if (now - _openedAtTicks < _options.BreakDuration.Ticks) return false;
                    TransitionTo(CircuitState.HalfOpen);
                    epoch = _epochMirror;
                    _halfOpenAttempts = 1; // the call that triggered the transition is the first probe
                    _lastProbeTicks = now;
                    enteredHalfOpen = true;
                    return true;

                default: // HalfOpen
                    if (_halfOpenAttempts < _options.HalfOpenMaxAttempts)
                    {
                        _halfOpenAttempts++;
                        _lastProbeTicks = now;
                        return true;
                    }

                    // Probes that never reported back (lost or hung) must not wedge the circuit: after
                    // BreakDuration without a verdict, admit a fresh probe.
                    if (now - _lastProbeTicks >= _options.BreakDuration.Ticks)
                    {
                        _halfOpenAttempts = 1;
                        _lastProbeTicks = now;
                        return true;
                    }

                    return false;
            }
        }
    }

    /// <summary>Gives back a HalfOpen probe slot when the call ended without a verdict.</summary>
    internal void ReleaseProbe(int epoch)
    {
        lock (_lock)
        {
            if (epoch == _epochMirror && _current == CircuitState.HalfOpen && _halfOpenAttempts > 0)
                _halfOpenAttempts--;
        }
    }

    /// <summary>Returns <c>true</c> when this call closed a HalfOpen circuit.</summary>
    internal bool RecordSuccessAndCheckClosed(int epoch)
    {
        lock (_lock)
        {
            if (epoch != _epochMirror) return false;

            AddToWindow(isFailure: false);
            if (_current != CircuitState.HalfOpen) return false;

            TransitionTo(CircuitState.Closed);
            return true;
        }
    }

    /// <summary>Returns <c>true</c> when this call opened the circuit.</summary>
    internal bool RecordFailureAndCheckOpened(int epoch)
    {
        lock (_lock)
        {
            if (epoch != _epochMirror) return false;

            AddToWindow(isFailure: true);
            if (_current == CircuitState.Open) return false;

            if (_current == CircuitState.HalfOpen || ShouldTrip())
            {
                _openedAtTicks = Now();
                TransitionTo(CircuitState.Open);
                return true;
            }

            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers (caller holds _lock)
    // -----------------------------------------------------------------------

    private long Now() => _clock().UtcTicks;

    private void AddToWindow(bool isFailure)
    {
        long now = Now();
        _window.Enqueue((now, isFailure));
        if (isFailure) _windowFailures++;
        PurgeOldEntries(now);
    }

    private void PurgeOldEntries(long now)
    {
        long cutoff = now - _options.SamplingDuration.Ticks;
        while (_window.Count > 0 && _window.Peek().Ticks < cutoff)
        {
            if (_window.Dequeue().IsFailure) _windowFailures--;
        }
    }

    private bool ShouldTrip()
    {
        PurgeOldEntries(Now());
        int total = _window.Count;

        if (_options.FailureRateThreshold > 0)
        {
            if (total < _options.MinimumThroughput) return false;
            return (double)_windowFailures / total >= _options.FailureRateThreshold;
        }

        return _windowFailures >= _options.FailureThreshold;
    }

    private void TransitionTo(CircuitState newState)
    {
        _current = newState;
        _halfOpenAttempts = 0;
        if (newState == CircuitState.Closed)
        {
            _window.Clear();
            _windowFailures = 0;
        }

        Volatile.Write(ref _stateMirror, (int)newState);
        Interlocked.Increment(ref _epochMirror);
    }
}
