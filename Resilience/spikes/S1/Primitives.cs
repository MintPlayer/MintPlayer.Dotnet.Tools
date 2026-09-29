using System.Collections.Concurrent;

namespace S1;

// Shared runtime pieces both of our designs use. Their costs are the intrinsic ones (timer queue, lock),
// kept comparable to Polly's so the benchmark measures composition, not a cheaper strategy.

public readonly struct Outcome<T>
{
    public Outcome(T result) { Result = result; Exception = null; }
    public Outcome(Exception exception) { Result = default!; Exception = exception; }
    public T Result { get; }
    public Exception? Exception { get; }
}

public static class Rejections
{
    // Cached, stackless rejection instances (the S5 idea); never thrown in S1.
    public static readonly Exception BrokenCircuit = new InvalidOperationException("Circuit is open.");
    public static readonly Exception Timeout = new TimeoutException("Attempt timed out.");
}

public static class Predicates
{
    public static bool ShouldHandle(in Outcome<int> o) => o.Exception is not null || o.Result == -1;
}

public sealed class PooledCts
{
    public readonly CancellationTokenSource Cts = new();
    public CancellationTokenRegistration Registration;
}

public static class CtsPool
{
    [ThreadStatic] private static PooledCts? t_cached;
    private static readonly ConcurrentQueue<PooledCts> s_queue = new();

    public static PooledCts Rent(TimeSpan timeout, CancellationToken parent)
    {
        var p = t_cached;
        if (p is not null) t_cached = null;
        else if (!s_queue.TryDequeue(out p)) p = new PooledCts();

        if (parent.CanBeCanceled)
            p.Registration = parent.UnsafeRegister(static s => ((CancellationTokenSource)s!).Cancel(), p.Cts);
        p.Cts.CancelAfter(timeout);
        return p;
    }

    public static void Return(PooledCts p)
    {
        p.Registration.Dispose();
        p.Registration = default;
        if (!p.Cts.TryReset()) { p.Cts.Dispose(); return; }
        if (t_cached is null) t_cached = p;
        else s_queue.Enqueue(p);
    }
}

/// <summary>Lock-based, like Polly's CircuitStateController. Lock-free is S4's question.</summary>
public sealed class CircuitBreaker(double failureRatio, int minimumThroughput, TimeSpan sampling, TimeSpan breakDuration, TimeProvider time)
{
    private readonly object _lock = new();
    private int _state; // 0 closed, 1 open, 2 half-open
    private DateTimeOffset _blockedUntil;
    private DateTimeOffset _windowStart = time.GetUtcNow();
    private int _successes, _failures;

    public bool TryEnter()
    {
        lock (_lock)
        {
            if (_state == 0) return true;
            if (_state == 1 && time.GetUtcNow() >= _blockedUntil) { _state = 2; return true; }
            return false;
        }
    }

    public void Record(bool failure)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            if (_state == 2)
            {
                if (failure) { _state = 1; _blockedUntil = now + breakDuration; }
                else { _state = 0; ResetWindow(now); }
                return;
            }
            if (now - _windowStart >= sampling) ResetWindow(now);
            if (failure) _failures++; else _successes++;
            var total = _failures + _successes;
            if (total >= minimumThroughput && (double)_failures / total >= failureRatio)
            {
                _state = 1;
                _blockedUntil = now + breakDuration;
            }
        }
    }

    private void ResetWindow(DateTimeOffset now) { _windowStart = now; _successes = 0; _failures = 0; }
}

public static class Backoff
{
    // Exponential with jitter, computed on an actual retry, as Polly does.
    public static TimeSpan Exponential(int attempt, TimeSpan baseDelay)
    {
        if (baseDelay <= TimeSpan.Zero) return TimeSpan.Zero;
        var exp = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        return TimeSpan.FromMilliseconds(exp * (0.5 + Random.Shared.NextDouble() / 2));
    }
}

public static class Config
{
    public static readonly TimeSpan OuterTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan InnerTimeout = TimeSpan.FromSeconds(2);
    public const int MaxRetries = 3;
    public static readonly TimeSpan RetryDelay = TimeSpan.Zero;
    // 0.9 so the 50 %-failure OneRetry workload keeps the circuit closed in every implementation.
    public const double FailureRatio = 0.9;
    public const int MinimumThroughput = 10;
    public static readonly TimeSpan Sampling = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Break = TimeSpan.FromSeconds(15);

    public static CircuitBreaker NewBreaker() =>
        new(FailureRatio, MinimumThroughput, Sampling, Break, TimeProvider.System);
}
