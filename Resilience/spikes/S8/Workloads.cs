namespace S8;

public sealed class Flip { public int Calls; }

/// <summary>Same workloads as S1 (S1's copy lives in its Benchmarks.cs, which drags in the other S1 variants).</summary>
public static class Callbacks
{
    public static readonly Func<Flip, CancellationToken, ValueTask<int>> Sync =
        static (_, _) => new ValueTask<int>(42);

    public static readonly Func<Flip, CancellationToken, ValueTask<int>> Async =
        static async (_, _) => { await Task.Yield(); return 42; };

    // Odd calls fail (-1, a handled result), even calls succeed: exactly one retry per execution.
    public static readonly Func<Flip, CancellationToken, ValueTask<int>> OneRetry =
        static (s, _) => new ValueTask<int>((++s.Calls & 1) == 1 ? -1 : 42);

    public static readonly Func<Flip, CancellationToken, ValueTask<int>> AlwaysFail =
        static (s, _) => { s.Calls++; return new ValueTask<int>(-1); };

    public static readonly Func<Flip, CancellationToken, ValueTask<int>> Hang =
        static async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return 42; };
}

/// <summary>A clock the test can move, for the breaker's half-open transition.</summary>
public sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
