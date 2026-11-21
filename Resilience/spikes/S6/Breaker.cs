namespace S6;

/// <summary>Circuit-breaker thresholds. Passed per call so a reload can change them without touching health state.</summary>
public readonly record struct BreakerSettings(double FailureRatio, int MinimumThroughput, TimeSpan Sampling, TimeSpan BreakDuration);

/// <summary>
/// Same lock-based controller as S1's <c>CircuitBreaker</c>, except that the thresholds are arguments instead of
/// constructor state. The controller holds only HEALTH state (circuit state, window counts, blocked-until), so a
/// reload that swaps the thresholds keeps it. All three variants (A/B/C) use this type, so the benchmark compares
/// only how the options are read.
/// </summary>
public sealed class Breaker(TimeProvider time)
{
    private readonly object _lock = new();
    private int _state; // 0 closed, 1 open, 2 half-open
    private DateTimeOffset _blockedUntil;
    private DateTimeOffset _windowStart = time.GetUtcNow();
    private int _successes, _failures;

    public Breaker() : this(TimeProvider.System) { }

    public int State { get { lock (_lock) return _state; } }

    public bool TryEnter()
    {
        lock (_lock)
        {
            if (_state == 0) return true;
            // _blockedUntil was computed with the BreakDuration in force when the circuit opened; a reload does
            // not shorten or extend a break that is already running.
            if (_state == 1 && time.GetUtcNow() >= _blockedUntil) { _state = 2; return true; }
            return false;
        }
    }

    public void Record(bool failure, in BreakerSettings s)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            if (_state == 2)
            {
                if (failure) { _state = 1; _blockedUntil = now + s.BreakDuration; }
                else { _state = 0; ResetWindow(now); }
                return;
            }
            if (now - _windowStart >= s.Sampling) ResetWindow(now);
            if (failure) _failures++; else _successes++;
            var total = _failures + _successes;
            if (total >= s.MinimumThroughput && (double)_failures / total >= s.FailureRatio)
            {
                _state = 1;
                _blockedUntil = now + s.BreakDuration;
            }
        }
    }

    /// <summary>Manual control (what an opt-in "reset circuit on reload" would call).</summary>
    public void Reset() { lock (_lock) { _state = 0; ResetWindow(time.GetUtcNow()); } }

    private void ResetWindow(DateTimeOffset now) { _windowStart = now; _successes = 0; _failures = 0; }
}
