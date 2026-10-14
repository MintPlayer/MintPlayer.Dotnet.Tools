namespace S4;

/// <summary>
/// Line-for-line port of Polly 8.8.0's CircuitStateController + AdvancedCircuitBehavior +
/// RollingHealthMetrics (one lock around everything, Queue of window objects), minus telemetry,
/// events and the break-duration generator. Same time source as the lock-free controller, so the
/// benchmark compares lock vs CAS and nothing else.
/// </summary>
public sealed class LockedCircuitBreaker : ICircuitBreaker
{
    private readonly object _lock = new();
    private readonly MicroClock _clock;
    private readonly double _failureRatio;
    private readonly int _minimumThroughput;
    private readonly long _breakMicros;
    private readonly long _windowMicros;
    private readonly long _samplingMicros;
    private readonly Queue<Window> _windows = new();
    private Window? _current;
    private CbState _state = CbState.Closed;
    private long _blockedUntil;

    public LockedCircuitBreaker(CbOptions options, TimeProvider time)
    {
        _clock = new MicroClock(time);
        _failureRatio = options.FailureRatio;
        _minimumThroughput = options.MinimumThroughput;
        _breakMicros = MicroClock.Micros(options.BreakDuration);
        _windowMicros = MicroClock.Micros(TimeSpan.FromTicks(options.SamplingDuration.Ticks / 10));
        _samplingMicros = MicroClock.Micros(options.SamplingDuration);
    }

    public CbState State { get { lock (_lock) return _state; } }

    public Admission TryEnter()
    {
        lock (_lock)
        {
            if (_state == CbState.Open)
            {
                var now = _clock.Now();
                if (now >= _blockedUntil)
                {
                    _blockedUntil = now + _breakMicros;
                    _state = CbState.HalfOpen;
                    return Admission.Probe;
                }
            }

            return _state == CbState.Closed ? Admission.Closed : Admission.Rejected;
        }
    }

    public void RecordSuccess(TimeSpan elapsed = default)
    {
        lock (_lock)
        {
            var now = _clock.Now();
            UpdateCurrentWindow(now).Successes++;
            if (_state == CbState.HalfOpen) CloseNeedsLock();
        }
    }

    public void RecordFailure(TimeSpan elapsed = default)
    {
        lock (_lock)
        {
            var now = _clock.Now();
            var shouldBreak = false;
            switch (_state)
            {
                case CbState.Closed:
                    UpdateCurrentWindow(now).Failures++;
                    var (s, f) = Health(now);
                    var total = s + f;
                    shouldBreak = total >= _minimumThroughput && (total == 0 ? 0 : f / (double)total) >= _failureRatio;
                    break;
                case CbState.Open:
                case CbState.Isolated:
                    UpdateCurrentWindow(now).Failures++;
                    break;
            }

            if (_state == CbState.HalfOpen || (_state == CbState.Closed && shouldBreak))
            {
                _blockedUntil = now + _breakMicros;
                _state = CbState.Open;
            }
        }
    }

    public void Isolate()
    {
        lock (_lock)
        {
            _blockedUntil = long.MaxValue;
            _state = CbState.Isolated;
        }
    }

    public void Close()
    {
        lock (_lock) CloseNeedsLock();
    }

    private void CloseNeedsLock()
    {
        _blockedUntil = 0;
        _state = CbState.Closed;
        _current = null;
        _windows.Clear();
    }

    private (int Successes, int Failures) Health(long now)
    {
        UpdateCurrentWindow(now);
        int s = 0, f = 0;
        foreach (var w in _windows) { s += w.Successes; f += w.Failures; }
        return (s, f);
    }

    private Window UpdateCurrentWindow(long now)
    {
        if (_current == null || now - _current.StartedAt >= _windowMicros)
        {
            _current = new Window { StartedAt = now };
            _windows.Enqueue(_current);
        }

        while (now - _windows.Peek().StartedAt >= _samplingMicros)
            _windows.Dequeue();

        return _current;
    }

    private sealed class Window
    {
        public int Successes;
        public int Failures;
        public long StartedAt;
    }
}
