using System.Runtime.CompilerServices;

namespace S4;

/// <summary>
/// Lock-free circuit-breaker controller with Polly 8.8.0 semantics (CircuitStateController +
/// AdvancedCircuitBehavior + RollingHealthMetrics).
///
/// The whole circuit state is ONE long, changed only by CompareExchange:
///   bits  0..1   CbState
///   bits  2..15  generation (+1 on every transition; defeats ABA, e.g. Closed(g) -> ... -> Closed(g'))
///   bits 16..63  blocked-until, microseconds since construction (2^48 us = 8.9 years; max = "forever")
/// Every transition is a CAS from the exact word the caller observed, so exactly one thread wins each
/// transition and fires its event. The half-open probe is the thread that wins Open -> HalfOpen.
/// </summary>
public sealed class LockFreeCircuitBreaker : ICircuitBreaker
{
    private const long StateMask = 3;
    private const int GenShift = 2;
    private const long GenMask = 0x3FFF;
    private const int UntilShift = 16;
    public const long Forever = (1L << 48) - 1;

    private readonly MicroClock _clock;
    private readonly StripedRollingHealth _health;
    private readonly double _failureRatio;
    private readonly int _minimumThroughput;
    private readonly long _breakMicros;
    private readonly long _slowTicks; // TimeSpan ticks; 0 = disabled
    private readonly double _slowRatio;

    // Keep the hot word on its own cache line (the health object is separate and striped).
    private PaddedLong _state;

    /// <summary>Test hook: (fromWord, toWord), invoked by the CAS winner of every transition.</summary>
    public Action<long, long>? OnTransition;

    public LockFreeCircuitBreaker(CbOptions options, TimeProvider time, int stripes = 0)
    {
        _clock = new MicroClock(time);
        _failureRatio = options.FailureRatio;
        _minimumThroughput = options.MinimumThroughput;
        _breakMicros = MicroClock.Micros(options.BreakDuration);
        _slowTicks = options.SlowCallThreshold.Ticks;
        _slowRatio = options.SlowCallRatio;
        _health = new StripedRollingHealth(options.SamplingDuration, stripes);
        _state.Value = Pack(CbState.Closed, 0, 0);
    }

    public CbState State => StateOf(Volatile.Read(ref _state.Value));
    public long StateWord => Volatile.Read(ref _state.Value);
    public (long Successes, long Failures, long Slow) Health() => _health.Snapshot(_clock.Now());

    public static CbState StateOf(long word) => (CbState)(word & StateMask);
    public static long GenOf(long word) => (word >> GenShift) & GenMask;
    public static long UntilOf(long word) => (long)((ulong)word >> UntilShift);

    private static long Pack(CbState state, long gen, long until)
        => (long)((ulong)until << UntilShift) | ((gen & GenMask) << GenShift) | (long)state;

    private static long Next(long from, CbState to, long until) => Pack(to, GenOf(from) + 1, until);

    private long OpenUntil(long now) => _breakMicros >= Forever - now ? Forever : now + _breakMicros;

    // ---------------------------------------------------------------- entry

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Admission TryEnter()
    {
        var s = Volatile.Read(ref _state.Value);
        return (s & StateMask) == (long)CbState.Closed ? Admission.Closed : TryEnterSlow(s);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Admission TryEnterSlow(long s)
    {
        while (true)
        {
            switch (StateOf(s))
            {
                case CbState.Closed:
                    return Admission.Closed;

                case CbState.Open:
                    var now = _clock.Now();
                    if (now < UntilOf(s))
                        return Admission.Rejected;

                    // Polly: _blockedUntil = now + breakDuration on entering half-open.
                    var halfOpen = Next(s, CbState.HalfOpen, OpenUntil(now));
                    var prev = Interlocked.CompareExchange(ref _state.Value, halfOpen, s);
                    if (prev == s)
                    {
                        // Only the probe records until the period ends, and nothing reads the window
                        // while half-open, so clear it now. This keeps pre-break failures from ever
                        // being visible after the close (see RESULTS.md "reset timing").
                        _health.Reset();
                        OnTransition?.Invoke(s, halfOpen);
                        return Admission.Probe;
                    }

                    s = prev;
                    continue;

                default: // HalfOpen (probe outstanding) or Isolated
                    return Admission.Rejected;
            }
        }
    }

    // ---------------------------------------------------------------- outcomes

    public void RecordSuccess(TimeSpan elapsed = default)
    {
        var now = _clock.Now();
        var slow = _slowTicks != 0 && elapsed.Ticks >= _slowTicks;

        // Polly: OnActionSuccess increments successes in every state.
        _health.Add(StripedRollingHealth.Success, now);
        if (slow) _health.Add(StripedRollingHealth.Slow, now);

        var s = Volatile.Read(ref _state.Value);
        while (true)
        {
            switch (StateOf(s))
            {
                case CbState.Closed:
                    if (slow && ShouldBreak(now))
                    {
                        var open = Next(s, CbState.Open, OpenUntil(now));
                        var p = Interlocked.CompareExchange(ref _state.Value, open, s);
                        if (p == s) { OnTransition?.Invoke(s, open); return; }
                        s = p;
                        continue;
                    }
                    return;

                case CbState.HalfOpen:
                {
                    // A success (from the probe OR a late call) ends the period: close.
                    // Slow-call extension: a slow probe counts as a failed probe.
                    var to = slow ? Next(s, CbState.Open, OpenUntil(now)) : Next(s, CbState.Closed, 0);
                    var p = Interlocked.CompareExchange(ref _state.Value, to, s);
                    if (p == s)
                    {
                        if (!slow) _health.Reset(); // Polly: OnCircuitClosed -> metrics.Reset()
                        OnTransition?.Invoke(s, to);
                        return;
                    }
                    s = p;
                    continue;
                }

                default: // Open / Isolated: late result, no state change.
                    return;
            }
        }
    }

    public void RecordFailure(TimeSpan elapsed = default)
    {
        var now = _clock.Now();
        var slow = _slowTicks != 0 && elapsed.Ticks >= _slowTicks;
        var counted = false;
        var s = Volatile.Read(ref _state.Value);

        while (true)
        {
            switch (StateOf(s))
            {
                case CbState.HalfOpen:
                {
                    // Polly: failure in half-open re-opens; the metric is not incremented.
                    var open = Next(s, CbState.Open, OpenUntil(now));
                    var p = Interlocked.CompareExchange(ref _state.Value, open, s);
                    if (p == s) { OnTransition?.Invoke(s, open); return; }
                    s = p;
                    continue;
                }

                case CbState.Closed:
                {
                    if (!counted)
                    {
                        _health.Add(StripedRollingHealth.Failure, now);
                        if (slow) _health.Add(StripedRollingHealth.Slow, now);
                        counted = true;
                    }

                    if (!ShouldBreak(now))
                        return;

                    var open = Next(s, CbState.Open, OpenUntil(now));
                    var p = Interlocked.CompareExchange(ref _state.Value, open, s);
                    if (p == s) { OnTransition?.Invoke(s, open); return; }
                    s = p; // lost the race: re-dispatch on the new state (never double-count)
                    continue;
                }

                default: // Open / Isolated: track the metric only (Polly), never extend the break.
                    if (!counted)
                    {
                        _health.Add(StripedRollingHealth.Failure, now);
                        if (slow) _health.Add(StripedRollingHealth.Slow, now);
                    }
                    return;
            }
        }
    }

    private bool ShouldBreak(long now)
    {
        var (successes, failures, slowCalls) = _health.Snapshot(now);
        var total = successes + failures;
        if (total == 0 || total < _minimumThroughput) return false;
        if (failures / (double)total >= _failureRatio) return true; // Polly HealthInfo.FailureRate
        return _slowTicks != 0 && slowCalls / (double)total >= _slowRatio;
    }

    // ---------------------------------------------------------------- manual control

    public void Isolate()
    {
        var s = Volatile.Read(ref _state.Value);
        while (true)
        {
            var to = Next(s, CbState.Isolated, Forever);
            var p = Interlocked.CompareExchange(ref _state.Value, to, s);
            if (p == s) { OnTransition?.Invoke(s, to); return; }
            s = p;
        }
    }

    public void Close()
    {
        _health.Reset(); // before publishing Closed: no one may judge the new Closed on old data
        var s = Volatile.Read(ref _state.Value);
        while (true)
        {
            var to = Next(s, CbState.Closed, 0);
            var p = Interlocked.CompareExchange(ref _state.Value, to, s);
            if (p == s) { _health.Reset(); OnTransition?.Invoke(s, to); return; }
            s = p;
        }
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 128)]
internal struct PaddedLong
{
    [System.Runtime.InteropServices.FieldOffset(64)] public long Value;
}

/// <summary>
/// Polly's RollingHealthMetrics, lock-free and striped.
///
/// Windows are event-anchored exactly like Polly's Queue&lt;HealthWindow&gt;: a new window starts at the
/// time of the first event that finds the current one older than samplingDuration/10, and a window
/// counts while now - start &lt; samplingDuration. Windows get a sequence number t; the ring holds the
/// last 16 (Polly never has more than 10 live, so no live window shares a slot).
///
///   _head      = (seq:32 | floor:32). floor &gt; seq means "no current window" (Polly's _currentWindow == null
///                after Reset). Windows below floor are dead.
///   _starts[i] = (tag:16 | start-us:48) for window t with t % 16 == i. Written (once, by CAS) BEFORE the
///                head advances to t, so a reader never sees a head whose start is missing.
///   counters   = per stripe, per slot: success/failure/slow as (window-seq:32 | count:32). A mismatched
///                tag means "older window": the first writer of the new window CAS-resets it. The full
///                32-bit tag means an idle stripe's ancient counts can never alias a live window.
/// Stripe = current processor id, so the hot success path touches a core-local cache line.
/// </summary>
internal sealed class StripedRollingHealth
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

    public StripedRollingHealth(TimeSpan samplingDuration, int stripes)
    {
        // Polly: _windowDuration = TimeSpan.FromTicks(samplingDuration.Ticks / 10)
        _windowMicros = MicroClock.Micros(TimeSpan.FromTicks(samplingDuration.Ticks / 10));
        _samplingMicros = MicroClock.Micros(samplingDuration);
        if (stripes <= 0) stripes = Math.Min(16, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Environment.ProcessorCount));
        _stripes = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)stripes);
        _stripeMask = _stripes - 1;
        _counters = new long[Lead + _stripes * StripeStride];
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
            return seq;
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
                return seq;

            var t = seq + 1;
            ref var slot = ref _starts[t & RingMask];
            var old = Volatile.Read(ref slot);
            var tag = StartTag(old);
            if (tag != (ushort)t)
            {
                // Slot tags stay within 16 of the head (every window writes its slot), so a 16-bit
                // modular comparison is sound. "Newer" means our head read is stale.
                if ((short)((ushort)t - tag) <= 0) continue;
                if (Volatile.Read(ref _head) != h) continue;
                // If this fails someone else started window t (or the head moved, which also rewrote
                // the slot): either way, re-read below.
                Interlocked.CompareExchange(ref slot, PackStart(t, now), old);
                if (StartTag(Volatile.Read(ref slot)) != (ushort)t) continue;
            }

            Interlocked.CompareExchange(ref _head, Head(t, floor), h);
        }
    }

    public void Add(int kind, long now)
    {
        var stripeBase = Lead + (Thread.GetCurrentProcessorId() & _stripeMask) * StripeStride + kind;
        while (true)
        {
            var t = CurrentWindow(now);
            ref var c = ref _counters[stripeBase + (int)(t & RingMask) * Kinds];
            while (true)
            {
                var v = Volatile.Read(ref c);
                if ((uint)((ulong)v >> 32) == t)
                {
                    if ((uint)v == uint.MaxValue) return; // saturate (Polly's int would overflow)
                    if (Interlocked.CompareExchange(ref c, v + 1, v) == v) return;
                    continue;
                }

                // Older window's leftovers (or ancient ones on an idle stripe) - unless our t is stale.
                if ((uint)((ulong)Volatile.Read(ref _head) >> 32) != t) break;
                if (Interlocked.CompareExchange(ref c, ((long)t << 32) | 1, v) == v) return;
            }
        }
    }

    /// <summary>Polly's _currentWindow = null; _windows.Clear().</summary>
    public void Reset()
    {
        while (true)
        {
            var h = Volatile.Read(ref _head);
            var seq = (uint)((ulong)h >> 32);
            if ((uint)h == seq + 1) return; // already empty
            if (Interlocked.CompareExchange(ref _head, Head(seq, seq + 1), h) == h) return;
        }
    }

    public (long Successes, long Failures, long Slow) Snapshot(long now)
    {
        var h = Volatile.Read(ref _head);
        var seq = (uint)((ulong)h >> 32);
        var floor = (uint)h;
        if (floor > seq) return default;

        var lo = seq - floor > Ring - 1 ? seq - (Ring - 1) : floor;
        long s = 0, f = 0, sl = 0;
        for (var u = lo; ; u++)
        {
            var st = Volatile.Read(ref _starts[u & RingMask]);
            if (StartTag(st) == (ushort)u && now - StartOf(st) < _samplingMicros)
            {
                var slotOffset = Lead + (int)(u & RingMask) * Kinds;
                for (var stripe = 0; stripe < _stripes; stripe++)
                {
                    var i = slotOffset + stripe * StripeStride;
                    var a = Volatile.Read(ref _counters[i + Success]);
                    if ((uint)((ulong)a >> 32) == u) s += (uint)a;
                    var b = Volatile.Read(ref _counters[i + Failure]);
                    if ((uint)((ulong)b >> 32) == u) f += (uint)b;
                    var c = Volatile.Read(ref _counters[i + Slow]);
                    if ((uint)((ulong)c >> 32) == u) sl += (uint)c;
                }
            }
            if (u == seq) break;
        }
        return (s, f, sl);
    }
}
