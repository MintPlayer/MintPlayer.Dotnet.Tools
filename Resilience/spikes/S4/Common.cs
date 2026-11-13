namespace S4;

/// <summary>Same numeric values as Polly's CircuitState.</summary>
public enum CbState { Closed = 0, Open = 1, HalfOpen = 2, Isolated = 3 }

public enum Admission : byte
{
    Rejected = 0,
    /// <summary>Admitted because the circuit is closed.</summary>
    Closed = 1,
    /// <summary>Admitted as THE half-open probe (this call performed Open -> HalfOpen).</summary>
    Probe = 2,
}

/// <param name="SlowCallThreshold">Zero disables slow-call breaking (resilience4j-style extension).</param>
public sealed record CbOptions(
    double FailureRatio,
    int MinimumThroughput,
    TimeSpan SamplingDuration,
    TimeSpan BreakDuration,
    TimeSpan SlowCallThreshold = default,
    double SlowCallRatio = 1.0);

public interface ICircuitBreaker
{
    Admission TryEnter();
    void RecordSuccess(TimeSpan elapsed = default);
    void RecordFailure(TimeSpan elapsed = default);
    void Isolate();
    void Close();
    CbState State { get; }
}

/// <summary>
/// Monotonic microseconds since construction, from <see cref="TimeProvider.GetTimestamp"/>.
/// Integer-only when the timestamp frequency is a multiple of 1 MHz (Windows 10 MHz, Linux 1 GHz,
/// FakeTimeProvider 10 MHz); Int128 fallback otherwise.
/// </summary>
public readonly struct MicroClock
{
    private readonly TimeProvider _time;
    private readonly long _origin;
    private readonly long _ticksPerMicro;
    private readonly long _frequency;

    public MicroClock(TimeProvider time)
    {
        _time = time;
        _origin = time.GetTimestamp();
        _frequency = time.TimestampFrequency;
        _ticksPerMicro = _frequency % 1_000_000 == 0 ? _frequency / 1_000_000 : 0;
    }

    public long Now()
    {
        var delta = _time.GetTimestamp() - _origin;
        return _ticksPerMicro != 0 ? delta / _ticksPerMicro : (long)((Int128)delta * 1_000_000 / _frequency);
    }

    /// <summary>TimeSpan -> microseconds, exactly as TimeSpan ticks (100 ns) truncate.</summary>
    public static long Micros(TimeSpan span) => span.Ticks / 10;
}

/// <summary>A TimeProvider whose time only moves when told to; safe to advance from many threads.</summary>
public sealed class ManualClock : TimeProvider
{
    private static readonly long Epoch = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Volatile.Read(ref _ticks);
    public override DateTimeOffset GetUtcNow() => new(Epoch + Volatile.Read(ref _ticks), TimeSpan.Zero);
    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
