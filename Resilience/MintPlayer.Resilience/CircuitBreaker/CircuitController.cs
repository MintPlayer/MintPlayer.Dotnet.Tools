using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MintPlayer.Resilience.CircuitBreaker;

/// <summary>What <see cref="CircuitController.TryEnter"/> decided for a call.</summary>
internal enum Admission : byte
{
    /// <summary>Rejected: open, isolated, or half-open with the probe outstanding.</summary>
    Rejected = 0,

    /// <summary>Admitted because the circuit is closed.</summary>
    Closed = 1,

    /// <summary>Admitted as THE half-open probe: this call performed Open → HalfOpen.</summary>
    Probe = 2,
}

/// <summary>A state change won by the caller: exactly one thread gets each one, and it raises the event.</summary>
internal readonly struct Transition
{
    public Transition(long from, long to, TimeSpan breakDuration)
    {
        Happened = true;
        From = from;
        To = to;
        BreakDuration = breakDuration;
    }

    public bool Happened { get; }

    public long From { get; }

    public long To { get; }

    /// <summary>For a transition to <see cref="CircuitState.Open"/>: how long the circuit stays open.</summary>
    public TimeSpan BreakDuration { get; }

    public CircuitState State => CircuitController.StateOf(To);

    public long Generation => CircuitController.GenOf(To);
}

/// <summary>
/// The lock-free circuit-breaker controller (plan S4), with Polly 8.8.0's semantics
/// (<c>CircuitStateController</c> + <c>AdvancedCircuitBehavior</c> + <c>RollingHealthMetrics</c>).
/// </summary>
/// <remarks>
/// <para>
/// The whole circuit state is ONE <see langword="long"/>, changed only by <c>CompareExchange</c>:
/// bits 0..1 = <see cref="CircuitState"/>, bits 2..15 = generation (+1 on every transition, which defeats
/// ABA such as Closed(g) → … → Closed(g')), bits 16..63 = blocked-until in µs since construction
/// (2^48 µs = 8.9 years; the maximum means "forever"). Every transition is a CAS from the exact word
/// the caller observed, so exactly one thread wins each transition and raises its event. The half-open
/// probe is the thread that wins Open → HalfOpen. Losing a CAS means re-dispatching on the new state,
/// never counting the outcome twice.
/// </para>
/// <para>
/// The one deliberate deviation from Polly: the health window is also cleared when the probe is
/// admitted, not only on close. A CAS cannot publish Closed and clear the window atomically, so a call
/// admitted right after the close could otherwise judge "should break" against the failures that broke
/// the circuit. Nothing reads the window while open or half-open, so the state sequence is unaffected;
/// only the break-duration generator's arguments could show it, which is why they are snapshot when
/// the circuit breaks from closed.
/// </para>
/// </remarks>
internal sealed class CircuitController
{
    private const long StateMask = 3;
    private const int GenShift = 2;
    internal const long GenMask = 0x3FFF;
    private const int UntilShift = 16;
    internal const long Forever = (1L << 48) - 1;

    /// <summary>The start time passed when a call's duration was not measured.</summary>
    public const long NotMeasured = long.MinValue;

    private readonly MicroClock _clock;
    private readonly RollingHealth _health;
    private readonly double _failureRatio;
    private readonly int _minimumThroughput;
    private readonly long _breakMicros;
    private readonly TimeSpan _breakDuration;
    private readonly long _slowMicros; // 0 = slow calls not tracked
    private readonly double _slowRatio;
    private readonly Func<BreakDurationGeneratorArguments, TimeSpan>? _breakDurationGenerator;

    // The hot word on its own cache line; the health object is separate and striped.
    private PaddedLong _state;

    // Polly's _breakingException: the exception of the last handled outcome (null for a handled result).
    private Exception? _lastException;

    // Polly's _halfOpenAttempts: probes since the last close, for the break-duration generator.
    private int _halfOpenAttempts;

    // The health window at the last Closed → Open, for the generator when a probe re-opens the circuit.
    private double _breakFailureRate;
    private int _breakFailureCount;

    public CircuitController(
        TimeProvider timeProvider,
        double failureRatio,
        int minimumThroughput,
        TimeSpan samplingDuration,
        TimeSpan breakDuration,
        TimeSpan? slowCallDurationThreshold = null,
        double slowCallRatio = 1.0,
        Func<BreakDurationGeneratorArguments, TimeSpan>? breakDurationGenerator = null,
        int stripes = 0)
    {
        _clock = new MicroClock(timeProvider);
        _failureRatio = failureRatio;
        _minimumThroughput = minimumThroughput;
        _breakDuration = breakDuration;
        _breakMicros = MicroClock.Micros(breakDuration);
        _slowMicros = slowCallDurationThreshold is { } threshold ? Math.Max(1, MicroClock.Micros(threshold)) : 0;
        _slowRatio = slowCallRatio;
        _breakDurationGenerator = breakDurationGenerator;
        _health = new RollingHealth(samplingDuration, stripes);
        _state.Value = Pack(CircuitState.Closed, 0, 0);
    }

    /// <summary>Serializes the circuit events in transition order (Polly runs them one at a time, in order).</summary>
    public EventSequencer Events { get; } = new();

    /// <summary>Test hook: (fromWord, toWord), invoked by the CAS winner of every transition.</summary>
    internal Action<long, long>? OnTransition;

    public CircuitState State => StateOf(Volatile.Read(ref _state.Value));

    /// <summary>Whether call durations must be measured (slow-call tracking is on).</summary>
    public bool MeasuresDuration => _slowMicros != 0;

    internal long StateWord => Volatile.Read(ref _state.Value);

    /// <summary>Test hook: the live health window as (successes, failures, slow calls).</summary>
    internal (long Successes, long Failures, long Slow) Health() => _health.Snapshot(_clock.Now());

    public static CircuitState StateOf(long word) => (CircuitState)(word & StateMask);

    public static long GenOf(long word) => (word >> GenShift) & GenMask;

    public static long UntilOf(long word) => (long)((ulong)word >> UntilShift);

    /// <summary>Monotonic microseconds since construction.</summary>
    public long Now() => _clock.Now();

    private static long Pack(CircuitState state, long gen, long until)
        => (long)((ulong)until << UntilShift) | ((gen & GenMask) << GenShift) | (long)state;

    private static long Next(long from, CircuitState to, long until) => Pack(to, GenOf(from) + 1, until);

    private static long OpenUntil(long now, long micros) => micros >= Forever - now ? Forever : now + micros;

    // ---------------------------------------------------------------- entry

    /// <summary>Decides whether a call may run. <paramref name="word"/> is the state the decision was made on (for a probe: the new half-open word).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Admission TryEnter(out long word)
    {
        var s = Volatile.Read(ref _state.Value);
        word = s;
        return (s & StateMask) == (long)CircuitState.Closed ? Admission.Closed : TryEnterSlow(ref word);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Admission TryEnterSlow(ref long word)
    {
        var s = word;
        while (true)
        {
            switch (StateOf(s))
            {
                case CircuitState.Closed:
                    word = s;
                    return Admission.Closed;

                case CircuitState.Open:
                    var now = _clock.Now();
                    if (now < UntilOf(s))
                    {
                        word = s;
                        return Admission.Rejected;
                    }

                    // Polly: _blockedUntil = now + breakDuration on entering half-open (never the generator).
                    var halfOpen = Next(s, CircuitState.HalfOpen, OpenUntil(now, _breakMicros));
                    var prev = Interlocked.CompareExchange(ref _state.Value, halfOpen, s);
                    if (prev == s)
                    {
                        // Only the probe is admitted until the period ends, and nothing reads the window
                        // while half-open, so clear it now: pre-break failures can never be seen after the
                        // close (the deliberate deviation, see the class remarks).
                        _health.Reset();
                        Interlocked.Increment(ref _halfOpenAttempts);
                        OnTransition?.Invoke(s, halfOpen);
                        word = halfOpen;
                        return Admission.Probe;
                    }

                    s = prev;
                    continue;

                default: // HalfOpen (probe outstanding) or Isolated
                    word = s;
                    return Admission.Rejected;
            }
        }
    }

    /// <summary>The rejection for a call that <see cref="TryEnter"/> rejected on <paramref name="word"/>. Allocates nothing.</summary>
    public Outcome<T> Reject<T>(long word)
    {
        if (StateOf(word) == CircuitState.Isolated)
        {
            return Outcome.Rejected<T>(RejectionKind.CircuitIsolated);
        }

        var remaining = UntilOf(word) - _clock.Now();
        var ticks = remaining <= 0 ? 0 : remaining >= long.MaxValue / 10 ? long.MaxValue : remaining * 10;
        return Outcome.Rejected<T>(RejectionKind.CircuitOpen, ticks, Volatile.Read(ref _lastException));
    }

    // ---------------------------------------------------------------- outcomes

    /// <summary>Records an unhandled outcome (a success). <paramref name="startMicros"/> is the admission time from <see cref="Now"/>, or <see cref="NotMeasured"/>.</summary>
    public Transition RecordSuccess(long startMicros, object? source)
    {
        var now = _clock.Now();
        var slow = IsSlow(startMicros, now);

        // Polly: OnActionSuccess increments successes in every state.
        _health.Add(RollingHealth.Success, now);
        if (slow)
        {
            _health.Add(RollingHealth.Slow, now);
        }

        var s = Volatile.Read(ref _state.Value);
        while (true)
        {
            switch (StateOf(s))
            {
                case CircuitState.Closed:
                {
                    // Fast successes never read the window; only a slow success can break the circuit.
                    if (!slow || !ShouldBreak(now, out var rate, out var count))
                    {
                        return default;
                    }

                    var breakDuration = BreakDurationFor(rate, count, source);
                    var open = Next(s, CircuitState.Open, OpenUntil(now, MicroClock.Micros(breakDuration)));
                    var p = Interlocked.CompareExchange(ref _state.Value, open, s);
                    if (p == s)
                    {
                        RememberBreak(rate, count);
                        OnTransition?.Invoke(s, open);
                        return new Transition(s, open, breakDuration);
                    }

                    s = p;
                    continue;
                }

                case CircuitState.HalfOpen:
                {
                    // A success (from the probe OR a late call) ends the period: close. Slow-call
                    // extension: a slow probe counts as a failed probe.
                    var breakDuration = slow ? BreakDurationFor(_breakFailureRate, _breakFailureCount, source) : TimeSpan.Zero;
                    var to = slow ? Next(s, CircuitState.Open, OpenUntil(now, MicroClock.Micros(breakDuration))) : Next(s, CircuitState.Closed, 0);
                    var p = Interlocked.CompareExchange(ref _state.Value, to, s);
                    if (p == s)
                    {
                        if (!slow)
                        {
                            _health.Reset(); // Polly: OnCircuitClosed → metrics.Reset()
                            Volatile.Write(ref _halfOpenAttempts, 0);
                        }

                        OnTransition?.Invoke(s, to);
                        return new Transition(s, to, breakDuration);
                    }

                    s = p;
                    continue;
                }

                default: // Open / Isolated: a late result, no state change.
                    return default;
            }
        }
    }

    /// <summary>Records a handled outcome (a failure) and the exception it carried (null for a handled result).</summary>
    public Transition RecordFailure(Exception? exception, long startMicros, object? source)
    {
        var now = _clock.Now();
        var slow = IsSlow(startMicros, now);
        var counted = false;

        // Polly: SetLastHandledOutcome in every state.
        Volatile.Write(ref _lastException, exception);

        var s = Volatile.Read(ref _state.Value);
        while (true)
        {
            switch (StateOf(s))
            {
                case CircuitState.HalfOpen:
                {
                    // Polly: a failure in half-open re-opens; the metric is not incremented.
                    var breakDuration = BreakDurationFor(_breakFailureRate, _breakFailureCount, source);
                    var open = Next(s, CircuitState.Open, OpenUntil(now, MicroClock.Micros(breakDuration)));
                    var p = Interlocked.CompareExchange(ref _state.Value, open, s);
                    if (p == s)
                    {
                        OnTransition?.Invoke(s, open);
                        return new Transition(s, open, breakDuration);
                    }

                    s = p;
                    continue;
                }

                case CircuitState.Closed:
                {
                    if (!counted)
                    {
                        _health.Add(RollingHealth.Failure, now);
                        if (slow)
                        {
                            _health.Add(RollingHealth.Slow, now);
                        }

                        counted = true;
                    }

                    if (!ShouldBreak(now, out var rate, out var count))
                    {
                        return default;
                    }

                    var breakDuration = BreakDurationFor(rate, count, source);
                    var open = Next(s, CircuitState.Open, OpenUntil(now, MicroClock.Micros(breakDuration)));
                    var p = Interlocked.CompareExchange(ref _state.Value, open, s);
                    if (p == s)
                    {
                        RememberBreak(rate, count);
                        OnTransition?.Invoke(s, open);
                        return new Transition(s, open, breakDuration);
                    }

                    s = p; // lost the race: re-dispatch on the new state (never double-count)
                    continue;
                }

                default: // Open / Isolated: track the metric only (Polly); never extend the break.
                    if (!counted)
                    {
                        _health.Add(RollingHealth.Failure, now);
                        if (slow)
                        {
                            _health.Add(RollingHealth.Slow, now);
                        }
                    }

                    return default;
            }
        }
    }

    private bool IsSlow(long startMicros, long now) => _slowMicros != 0 && startMicros != NotMeasured && now - startMicros >= _slowMicros;

    private bool ShouldBreak(long now, out double failureRate, out int failureCount)
    {
        var (successes, failures, slowCalls) = _health.Snapshot(now);
        var total = successes + failures;
        failureCount = (int)Math.Min(failures, int.MaxValue);
        failureRate = total == 0 ? 0 : failures / (double)total;
        if (total == 0 || total < _minimumThroughput)
        {
            return false;
        }

        if (failureRate >= _failureRatio)
        {
            return true; // Polly: Throughput >= MinimumThroughput && FailureRate >= FailureRatio
        }

        return _slowMicros != 0 && slowCalls / (double)total >= _slowRatio;
    }

    private TimeSpan BreakDurationFor(double failureRate, int failureCount, object? source)
    {
        if (_breakDurationGenerator is null)
        {
            return _breakDuration;
        }

        var duration = _breakDurationGenerator(new BreakDurationGeneratorArguments(failureRate, failureCount, source, Volatile.Read(ref _halfOpenAttempts)));
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    private void RememberBreak(double failureRate, int failureCount)
    {
        _breakFailureRate = failureRate;
        _breakFailureCount = failureCount;
    }

    // ---------------------------------------------------------------- manual control

    /// <summary>Holds the circuit open until <see cref="Close"/>. Always a transition (Polly raises OnOpened on every isolate).</summary>
    public Transition Isolate()
    {
        var s = Volatile.Read(ref _state.Value);
        while (true)
        {
            var to = Next(s, CircuitState.Isolated, Forever);
            var p = Interlocked.CompareExchange(ref _state.Value, to, s);
            if (p == s)
            {
                OnTransition?.Invoke(s, to);
                return new Transition(s, to, TimeSpan.MaxValue);
            }

            s = p;
        }
    }

    /// <summary>Closes the circuit and clears the health window. No transition when it was already closed (Polly raises no OnClosed then).</summary>
    public Transition Close()
    {
        _health.Reset(); // before publishing Closed: no one may judge the new Closed on old data
        Volatile.Write(ref _halfOpenAttempts, 0);
        var s = Volatile.Read(ref _state.Value);
        while (true)
        {
            if (StateOf(s) == CircuitState.Closed)
            {
                return default;
            }

            var to = Next(s, CircuitState.Closed, 0);
            var p = Interlocked.CompareExchange(ref _state.Value, to, s);
            if (p == s)
            {
                _health.Reset();
                OnTransition?.Invoke(s, to);
                return new Transition(s, to, TimeSpan.Zero);
            }

            s = p;
        }
    }
}

/// <summary>
/// Runs the circuit events one at a time in transition (generation) order, like Polly's scheduled
/// executor. The winner of transition <c>g</c> waits until the events of <c>g − 1</c> are done, runs
/// its own, then releases <c>g + 1</c>. Only transitions pass through here, so it costs nothing on the
/// call path; a waiter allocates only when two transitions' events overlap.
/// </summary>
internal sealed class EventSequencer
{
    private readonly Lock _lock = new();
    private long _completed; // generation of the last transition whose events are done (the initial word is gen 0)
    private List<(long Generation, TaskCompletionSource Signal)>? _waiters;

    public ValueTask WaitTurnAsync(long generation)
    {
        lock (_lock)
        {
            if (((_completed + 1) & CircuitController.GenMask) == generation)
            {
                return default;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            (_waiters ??= []).Add((generation, signal));
            return new ValueTask(signal.Task);
        }
    }

    public void Complete(long generation)
    {
        TaskCompletionSource? next = null;
        lock (_lock)
        {
            _completed = generation;
            if (_waiters is { Count: > 0 } waiters)
            {
                var wanted = (generation + 1) & CircuitController.GenMask;
                for (var i = 0; i < waiters.Count; i++)
                {
                    if (waiters[i].Generation == wanted)
                    {
                        next = waiters[i].Signal;
                        waiters.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        next?.TrySetResult();
    }
}

[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct PaddedLong
{
    [FieldOffset(64)]
    public long Value;
}

/// <summary>
/// Monotonic microseconds since construction, from <see cref="TimeProvider.GetTimestamp"/>. Integer-only
/// when the timestamp frequency is a multiple of 1 MHz (Windows 10 MHz, Linux 1 GHz, FakeTimeProvider
/// 10 MHz); an <see cref="Int128"/> fallback otherwise.
/// </summary>
internal readonly struct MicroClock
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

    /// <summary>TimeSpan → microseconds, truncating exactly as TimeSpan ticks (100 ns) do.</summary>
    public static long Micros(TimeSpan span) => span.Ticks / 10;
}

/// <summary>
/// Polly's <c>RollingHealthMetrics</c>, lock-free and striped (plan S4).
/// </summary>
/// <remarks>
/// <para>
/// Windows are event-anchored exactly like Polly's <c>Queue&lt;HealthWindow&gt;</c>: a new window starts at
/// the time of the first event that finds the current one at least samplingDuration/10 old, and a window
/// counts while now − start &lt; samplingDuration. Windows get a sequence number t; the ring holds the
/// last 16 (Polly never has more than 10 live, so no live window shares a slot).
/// </para>
/// <list type="bullet">
/// <item><c>_head = (seq:32 | floor:32)</c>. floor &gt; seq means "no current window" (Polly's
/// <c>_currentWindow == null</c> after Reset). Windows below floor are dead.</item>
/// <item><c>_starts[i] = (tag:16 | start-µs:48)</c> for window t with t % 16 == i. Written once, by CAS,
/// BEFORE the head advances to t, so a reader never sees a head whose start is missing.</item>
/// <item>Counters: per stripe, per slot, success/failure/slow as <c>(window-seq:32 | count:32)</c>. A
/// mismatched tag means "older window": the first writer of the new window CAS-resets it. The full
/// 32-bit tag means an idle stripe's ancient counts can never alias a live window.</item>
/// </list>
/// <para>Stripe = current processor id, so the hot success path touches a core-local cache line.</para>
/// </remarks>
internal sealed class RollingHealth
{
    public const int Success = 0, Failure = 1, Slow = 2;
    private const int Ring = 16, RingMask = Ring - 1, Kinds = 3;
    private const int StripeStride = Ring * Kinds + 8; // +8 longs so neighbouring stripes never share a line
    private const int Lead = 8;
    private const long StartMask = (1L << 48) - 1;

    private readonly long _windowMicros;
    private readonly long _samplingMicros;
    private readonly long[] _starts = new long[Ring];
    private readonly long[] _counters;
    private readonly int _stripeMask;
    private readonly int _stripes;
    private long _head;

    public RollingHealth(TimeSpan samplingDuration, int stripes)
    {
        // Polly: _windowDuration = TimeSpan.FromTicks(samplingDuration.Ticks / 10)
        _windowMicros = MicroClock.Micros(TimeSpan.FromTicks(samplingDuration.Ticks / 10));
        _samplingMicros = MicroClock.Micros(samplingDuration);
        if (stripes <= 0)
        {
            stripes = Math.Min(16, (int)BitOperations.RoundUpToPowerOf2((uint)Environment.ProcessorCount));
        }

        _stripes = (int)BitOperations.RoundUpToPowerOf2((uint)stripes);
        _stripeMask = _stripes - 1;
        _counters = new long[Lead + (_stripes * StripeStride)];
        _head = Head(0, 1); // empty
    }

    private static long Head(uint seq, uint floor) => ((long)seq << 32) | floor;

    private static long PackStart(uint t, long start) => ((long)(ushort)t << 48) | (start & StartMask);

    private static ushort StartTag(long v) => (ushort)((ulong)v >> 48);

    private static long StartOf(long v) => v & StartMask;

    /// <summary>Returns the sequence number of the window the event at <paramref name="now"/> belongs to.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint CurrentWindow(long now)
    {
        var h = Volatile.Read(ref _head);
        var seq = (uint)((ulong)h >> 32);
        if ((uint)h <= seq && now - StartOf(Volatile.Read(ref _starts[seq & RingMask])) < _windowMicros)
        {
            return seq;
        }

        return AdvanceWindow(now);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private uint AdvanceWindow(long now)
    {
        while (true)
        {
            var h = Volatile.Read(ref _head);
            var seq = (uint)((ulong)h >> 32);
            var floor = (uint)h;
            if (floor <= seq && now - StartOf(Volatile.Read(ref _starts[seq & RingMask])) < _windowMicros)
            {
                return seq;
            }

            var t = seq + 1;
            ref var slot = ref _starts[t & RingMask];
            var old = Volatile.Read(ref slot);
            var tag = StartTag(old);
            if (tag != (ushort)t)
            {
                // Slot tags stay within 16 of the head (every window writes its slot), so a 16-bit
                // modular comparison is sound. "Newer" means our head read is stale.
                if ((short)((ushort)t - tag) <= 0)
                {
                    continue;
                }

                if (Volatile.Read(ref _head) != h)
                {
                    continue;
                }

                // If this fails someone else started window t (or the head moved, which also rewrote
                // the slot): either way, re-read below.
                Interlocked.CompareExchange(ref slot, PackStart(t, now), old);
                if (StartTag(Volatile.Read(ref slot)) != (ushort)t)
                {
                    continue;
                }
            }

            Interlocked.CompareExchange(ref _head, Head(t, floor), h);
        }
    }

    public void Add(int kind, long now)
    {
        var stripeBase = Lead + ((Thread.GetCurrentProcessorId() & _stripeMask) * StripeStride) + kind;
        while (true)
        {
            var t = CurrentWindow(now);
            ref var c = ref _counters[stripeBase + ((int)(t & RingMask) * Kinds)];
            while (true)
            {
                var v = Volatile.Read(ref c);
                if ((uint)((ulong)v >> 32) == t)
                {
                    if ((uint)v == uint.MaxValue)
                    {
                        return; // saturate (Polly's int would overflow)
                    }

                    if (Interlocked.CompareExchange(ref c, v + 1, v) == v)
                    {
                        return;
                    }

                    continue;
                }

                // Older window's leftovers (or ancient ones on an idle stripe), unless our t is stale.
                if ((uint)((ulong)Volatile.Read(ref _head) >> 32) != t)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref c, ((long)t << 32) | 1, v) == v)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Polly's <c>_currentWindow = null; _windows.Clear()</c>.</summary>
    public void Reset()
    {
        while (true)
        {
            var h = Volatile.Read(ref _head);
            var seq = (uint)((ulong)h >> 32);
            if ((uint)h == seq + 1)
            {
                return; // already empty
            }

            if (Interlocked.CompareExchange(ref _head, Head(seq, seq + 1), h) == h)
            {
                return;
            }
        }
    }

    public (long Successes, long Failures, long Slow) Snapshot(long now)
    {
        var h = Volatile.Read(ref _head);
        var seq = (uint)((ulong)h >> 32);
        var floor = (uint)h;
        if (floor > seq)
        {
            return default;
        }

        var lo = seq - floor > Ring - 1 ? seq - (Ring - 1) : floor;
        long s = 0, f = 0, sl = 0;
        for (var u = lo; ; u++)
        {
            var st = Volatile.Read(ref _starts[u & RingMask]);
            if (StartTag(st) == (ushort)u && now - StartOf(st) < _samplingMicros)
            {
                var slotOffset = Lead + ((int)(u & RingMask) * Kinds);
                for (var stripe = 0; stripe < _stripes; stripe++)
                {
                    var i = slotOffset + (stripe * StripeStride);
                    var a = Volatile.Read(ref _counters[i + Success]);
                    if ((uint)((ulong)a >> 32) == u)
                    {
                        s += (uint)a;
                    }

                    var b = Volatile.Read(ref _counters[i + Failure]);
                    if ((uint)((ulong)b >> 32) == u)
                    {
                        f += (uint)b;
                    }

                    var c = Volatile.Read(ref _counters[i + Slow]);
                    if ((uint)((ulong)c >> 32) == u)
                    {
                        sl += (uint)c;
                    }
                }
            }

            if (u == seq)
            {
                break;
            }
        }

        return (s, f, sl);
    }
}
