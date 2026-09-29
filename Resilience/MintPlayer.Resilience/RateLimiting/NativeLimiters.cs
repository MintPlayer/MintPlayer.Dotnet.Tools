using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.RateLimiting;

/// <summary>
/// A limiter written for this library rather than taken from <c>System.Threading.RateLimiting</c>: it
/// hands out no lease object, so neither an admission nor a rejection allocates (plan S5). Thread-safe
/// and lock-free; one instance per <c>Build()</c>, shared by every result type of the pipeline.
/// </summary>
internal abstract class NativeLimiter
{
    /// <summary>No retry-after hint.</summary>
    public const long NoRetryAfter = -1;

    /// <summary>Tries to take one permit; on refusal, <paramref name="retryAfterTicks"/> is the hint in <see cref="TimeSpan"/> ticks, or <see cref="NoRetryAfter"/>.</summary>
    public abstract bool TryAcquire(out long retryAfterTicks);

    /// <summary>Whether a permit must be handed back when the call finishes (concurrency), rather than expiring (windows).</summary>
    public virtual bool ReleasesOnExit => false;

    /// <summary>Hands back the permit of a finished call; only called when <see cref="ReleasesOnExit"/>.</summary>
    public virtual void Release()
    {
    }
}

/// <summary>
/// At most <c>permitLimit</c> admissions per window; windows are consecutive, aligned to the time the
/// pipeline was built. The state is one <see cref="long"/> (window number in the high 32 bits, admissions
/// in the low 32), changed by compare-and-swap, so the limit is exact under any contention.
/// </summary>
internal sealed class FixedWindowLimiter : NativeLimiter
{
    private readonly MicroClock _clock;
    private readonly long _windowMicros;
    private readonly uint _permitLimit;
    private long _state;

    public FixedWindowLimiter(TimeProvider timeProvider, int permitLimit, TimeSpan window)
    {
        _clock = new MicroClock(timeProvider);
        _windowMicros = Math.Max(1, MicroClock.Micros(window));
        _permitLimit = (uint)permitLimit;
    }

    public override bool TryAcquire(out long retryAfterTicks)
    {
        while (true)
        {
            var now = _clock.Now();
            var window = now / _windowMicros;
            var id = (uint)window;
            var current = Volatile.Read(ref _state);
            var currentId = (uint)(current >>> 32);
            long next;
            if (currentId == id)
            {
                var count = (uint)current;
                if (count >= _permitLimit)
                {
                    retryAfterTicks = ((window + 1) * _windowMicros - now) * 10;
                    return false;
                }

                next = current + 1;
            }
            else if ((int)(id - currentId) > 0)
            {
                next = ((long)id << 32) | 1;
            }
            else
            {
                // Another thread read a later clock and already moved to the next window: read the clock again.
                continue;
            }

            if (Interlocked.CompareExchange(ref _state, next, current) == current)
            {
                retryAfterTicks = NoRetryAfter;
                return true;
            }
        }
    }
}

/// <summary>
/// At most <c>permitLimit</c> admissions in any window of <c>segmentsPerWindow</c> consecutive segments
/// (the current one included), over a <see cref="SegmentedCounter"/>. An admission is counted first and
/// taken back when the window turns out to be full, so the limit is never exceeded; under contention a
/// call may be refused while another call's increment is being taken back.
/// </summary>
internal sealed class SlidingWindowLimiter : NativeLimiter
{
    private readonly MicroClock _clock;
    private readonly long _segmentMicros;
    private readonly long _permitLimit;
    private readonly SegmentedCounter _counter;

    public SlidingWindowLimiter(TimeProvider timeProvider, int permitLimit, TimeSpan window, int segmentsPerWindow)
    {
        _clock = new MicroClock(timeProvider);
        _segmentMicros = Math.Max(1, MicroClock.Micros(window) / segmentsPerWindow);
        _permitLimit = permitLimit;
        _counter = new SegmentedCounter(segmentsPerWindow);
    }

    public override bool TryAcquire(out long retryAfterTicks)
    {
        while (true)
        {
            var now = _clock.Now();
            var segment = now / _segmentMicros;
            if (!_counter.TryIncrement(segment))
            {
                continue;
            }

            if (_counter.Sum(segment) <= _permitLimit)
            {
                retryAfterTicks = NoRetryAfter;
                return true;
            }

            _counter.Decrement(segment);
            var segments = Math.Max(1, _counter.SegmentsUntilOldestExpires(segment));
            retryAfterTicks = ((segment + segments) * _segmentMicros - now) * 10;
            return false;
        }
    }
}

/// <summary>At most <c>permitLimit</c> calls in flight; no queue. A compare-and-swap loop on one counter.</summary>
internal sealed class NativeConcurrencyLimiter(int permitLimit) : NativeLimiter
{
    private int _inFlight;

    /// <summary>The number of calls holding a permit.</summary>
    public int InFlight => Volatile.Read(ref _inFlight);

    public override bool ReleasesOnExit => true;

    public override bool TryAcquire(out long retryAfterTicks)
    {
        retryAfterTicks = NoRetryAfter;
        var current = Volatile.Read(ref _inFlight);
        while (current < permitLimit)
        {
            var seen = Interlocked.CompareExchange(ref _inFlight, current + 1, current);
            if (seen == current)
            {
                return true;
            }

            current = seen;
        }

        return false;
    }

    public override void Release() => Interlocked.Decrement(ref _inFlight);
}

/// <summary>
/// Any <see cref="NativeLimiter"/> as interpreter hooks. A refusal is
/// <c>Outcome.Rejected(RateLimited, retry-after)</c>, with no exception and no lease. Slot use: none.
/// </summary>
internal sealed class NativeLimiterStrategy<T>(NativeLimiter limiter, Func<OnLimiterRejectedArguments, ValueTask>? onRejected) : PipelineStrategy<T>
{
    public override ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index)
    {
        if (limiter.TryAcquire(out var retryAfterTicks))
        {
            return new(true);
        }

        frame.Outcome = Outcome.Rejected<T>(RejectionKind.RateLimited, retryAfterTicks);
        return onRejected is null ? new(false) : LimiterEvents.RaiseRejectedAsync(onRejected, frame, retryAfterTicks);
    }

    public override ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index)
    {
        if (limiter.ReleasesOnExit)
        {
            limiter.Release();
        }

        return new(false);
    }
}

/// <summary>A result-agnostic <see cref="NativeLimiter"/> strategy: the limiter is created once per <c>Build()</c>.</summary>
internal sealed class NativeLimiterStrategyFactory(NativeLimiter limiter, Func<OnLimiterRejectedArguments, ValueTask>? onRejected) : StrategyFactory
{
    public NativeLimiter Limiter { get; } = limiter;

    public override PipelineStrategy<TResult> Create<TResult>() => new NativeLimiterStrategy<TResult>(Limiter, onRejected);
}

/// <summary>The rejection event of the lease-free limiters.</summary>
internal static class LimiterEvents
{
    [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<bool> RaiseRejectedAsync(Func<OnLimiterRejectedArguments, ValueTask> onRejected, ExecutionFrame frame, long retryAfterTicks)
    {
        TimeSpan? retryAfter = retryAfterTicks >= 0 ? TimeSpan.FromTicks(retryAfterTicks) : null;
        await onRejected(new OnLimiterRejectedArguments(frame, retryAfter)).ConfigureAwait(frame.ContinueOnCapturedContext);
        return false;
    }
}
