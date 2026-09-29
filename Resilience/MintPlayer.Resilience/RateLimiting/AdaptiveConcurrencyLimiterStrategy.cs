using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.RateLimiting;

/// <summary>What an admitted call must hand back when it finishes.</summary>
internal readonly record struct AdaptivePermit(long StartMicros, int InFlight, int Generation);

/// <summary>
/// The shared state of one adaptive concurrency limiter: the in-flight count, the limit, the backoff
/// generation and (for the gradient algorithm) the no-load latency estimate. Lock-free; never allocates
/// after construction.
/// </summary>
/// <remarks>
/// <para>
/// <b>One backoff per generation.</b> Calls that were admitted together also fail together: a burst of
/// drops from one overload episode must shrink the limit once, not once per call (TCP's "one decrease
/// per round trip"). Every admitted call remembers the backoff generation it was admitted in, and only
/// the first drop of the current generation wins the compare-and-swap that starts the next generation
/// and backs off. Calls admitted before that backoff cannot back off again.
/// </para>
/// <para>
/// <b>Utilization.</b> A sample only raises the limit when the limiter was at least half used, measured
/// as the larger of the in-flight count at admission and at completion.
/// </para>
/// </remarks>
internal sealed class AdaptiveLimitController
{
    private readonly MicroClock _clock;
    private readonly AdaptiveConcurrencyAlgorithm _algorithm;
    private readonly double _minLimit;
    private readonly double _maxLimit;
    private readonly double _backoffRatio;
    private readonly double _smoothing;
    private readonly double _tolerance;
    private readonly double _decay;
    private readonly double _floorLimit;
    private readonly long _thresholdMicros;

    private int _inFlight;
    private int _generation;
    private long _limitBits;
    private long _noLoadLatencyBits;

    public AdaptiveLimitController(
        TimeProvider timeProvider,
        AdaptiveConcurrencyAlgorithm algorithm,
        int initialLimit,
        int minLimit,
        int maxLimit,
        double backoffRatio,
        double smoothing,
        double tolerance,
        int latencyWindow,
        TimeSpan? latencyThreshold)
    {
        _clock = new MicroClock(timeProvider);
        _algorithm = algorithm;
        _minLimit = minLimit;
        _maxLimit = maxLimit;
        _backoffRatio = backoffRatio;
        _smoothing = smoothing;
        _tolerance = tolerance;
        _decay = 1.0 + 1.0 / latencyWindow;
        _floorLimit = Math.Max(minLimit, 4) + 1;
        _thresholdMicros = latencyThreshold is { } threshold ? Math.Max(1, MicroClock.Micros(threshold)) : 0;
        _limitBits = BitConverter.DoubleToInt64Bits(initialLimit);
    }

    /// <summary>The current limit, as used for admission (the fractional limit rounded down).</summary>
    public int Limit => (int)ExactLimit;

    /// <summary>The current limit before rounding.</summary>
    public double ExactLimit => BitConverter.Int64BitsToDouble(Volatile.Read(ref _limitBits));

    /// <summary>The number of admitted calls that have not finished.</summary>
    public int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>How many times the limit has backed off.</summary>
    public int Generation => Volatile.Read(ref _generation);

    /// <summary>The gradient algorithm's no-load latency estimate in µs (0 before the first sample).</summary>
    public double NoLoadLatencyMicros => BitConverter.Int64BitsToDouble(Volatile.Read(ref _noLoadLatencyBits));

    /// <summary>Admits a call when fewer than <see cref="Limit"/> are in flight.</summary>
    public bool TryAcquire(out AdaptivePermit permit)
    {
        var generation = Volatile.Read(ref _generation);
        var current = Volatile.Read(ref _inFlight);
        while (current < Limit)
        {
            var seen = Interlocked.CompareExchange(ref _inFlight, current + 1, current);
            if (seen == current)
            {
                permit = new AdaptivePermit(_clock.Now(), current + 1, generation);
                return true;
            }

            current = seen;
        }

        permit = default;
        return false;
    }

    /// <summary>Records a finished call, adjusts the limit, and releases the permit.</summary>
    /// <param name="permit">The permit from <see cref="TryAcquire"/>.</param>
    /// <param name="handled">Whether the outcome was a drop by the strategy's predicate.</param>
    public void Release(in AdaptivePermit permit, bool handled)
    {
        var latency = Math.Max(1, _clock.Now() - permit.StartMicros);
        var dropped = handled || (_thresholdMicros != 0 && latency > _thresholdMicros);
        Sample(latency, dropped, Math.Max(permit.InFlight, Volatile.Read(ref _inFlight)), permit.Generation);
        Interlocked.Decrement(ref _inFlight);
    }

    /// <summary>Releases the permit of a call that produced no sample.</summary>
    public void ReleaseWithoutSample() => Interlocked.Decrement(ref _inFlight);

    /// <summary>Applies one sample: its latency, whether it was a drop, the in-flight count it ran with, and its admission generation.</summary>
    internal void Sample(long latencyMicros, bool dropped, int inFlight, int generation)
    {
        var limitNow = ExactLimit;
        var lightlyLoaded = inFlight * 2 < limitNow;
        var noLoadLatency = _algorithm == AdaptiveConcurrencyAlgorithm.Gradient
            ? UpdateNoLoadLatency(latencyMicros, forget: limitNow <= _floorLimit)
            : 0;

        if (dropped)
        {
            // Only the first drop of the generation backs off; the rest of its burst is the same signal.
            if (generation == Volatile.Read(ref _generation) && Interlocked.CompareExchange(ref _generation, generation + 1, generation) == generation)
            {
                Update(static (limit, self, _, _) => limit * self._backoffRatio, 0, 0);
            }

            return;
        }

        if (lightlyLoaded)
        {
            // Application-limited: the sample says nothing about a higher limit.
            return;
        }

        if (_algorithm == AdaptiveConcurrencyAlgorithm.Aimd)
        {
            // About +1 per round trip: every call of a full limit's worth adds 1 / limit.
            Update(static (limit, _, _, _) => limit + 1 / limit, 0, 0);
        }
        else
        {
            Update(
                static (limit, self, noLoad, latency) =>
                {
                    var gradient = Math.Clamp(self._tolerance * noLoad / latency, 0.5, 1.0);
                    var target = limit * gradient + Math.Sqrt(limit);

                    // A full limit's worth of samples moves the limit by Smoothing towards the target:
                    // one step per round trip, however many calls share it.
                    return limit + (target - limit) * self._smoothing / limit;
                },
                noLoadLatency,
                latencyMicros);
        }
    }

    private void Update(Func<double, AdaptiveLimitController, double, double, double> step, double a, double b)
    {
        while (true)
        {
            var currentBits = Volatile.Read(ref _limitBits);
            var next = Math.Clamp(step(BitConverter.Int64BitsToDouble(currentBits), this, a, b), _minLimit, _maxLimit);
            var nextBits = BitConverter.DoubleToInt64Bits(next);
            if (nextBits == currentBits || Interlocked.CompareExchange(ref _limitBits, nextBits, currentBits) == currentBits)
            {
                return;
            }
        }
    }

    /// <summary>
    /// The no-load latency: the lowest latency seen. It is only forgotten while the limit sits at its
    /// floor (<c>_floorLimit</c> = max(MinLimit, 4) + 1; 4 is where the √limit headroom balances the
    /// steepest gradient, 0.5, so a stale estimate pins the limit just above 4): each such sample lets
    /// the estimate rise by a factor <c>1 + 1 / LatencyWindow</c> before taking the minimum. Any other
    /// sample may include queueing that the limit caused, and forgetting on it lets the estimate chase
    /// the load and the limit creep up under a sustained overload (measured: forgetting on "lightly
    /// loaded" samples drifted the limit to its maximum, because a burst released in reverse order makes
    /// half its samples look lightly loaded). So a baseline that rose by more than <c>Tolerance</c> first
    /// drives the limit down to its floor, and is re-learned there.
    /// </summary>
    private double UpdateNoLoadLatency(long latencyMicros, bool forget)
    {
        while (true)
        {
            var currentBits = Volatile.Read(ref _noLoadLatencyBits);
            var current = BitConverter.Int64BitsToDouble(currentBits);
            var next = current == 0 ? latencyMicros : Math.Min(latencyMicros, forget ? current * _decay : current);
            if (Interlocked.CompareExchange(ref _noLoadLatencyBits, BitConverter.DoubleToInt64Bits(next), currentBits) == currentBits)
            {
                return next;
            }
        }
    }
}

/// <summary>
/// The adaptive concurrency limiter as interpreter hooks. Slot use: <c>Long</c> = admission time in µs,
/// <c>Int</c> = admission generation, <c>Double</c> = in-flight count at admission.
/// </summary>
internal sealed class AdaptiveConcurrencyLimiterStrategy<T>(
    AdaptiveLimitController controller,
    Func<AdaptiveConcurrencyLimiterPredicateArguments<T>, bool> shouldHandle,
    Func<OnLimiterRejectedArguments, ValueTask>? onRejected) : PipelineStrategy<T>
{
    public AdaptiveLimitController Controller => controller;

    public override ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index)
    {
        if (controller.TryAcquire(out var permit))
        {
            ref var slot = ref frame.Slots[index];
            slot.Long = permit.StartMicros;
            slot.Int = permit.Generation;
            slot.Double = permit.InFlight;
            return new(true);
        }

        frame.Outcome = Outcome.Rejected<T>(RejectionKind.RateLimited);
        return onRejected is null ? new(false) : LimiterEvents.RaiseRejectedAsync(onRejected, frame, NativeLimiter.NoRetryAfter);
    }

    public override ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index)
    {
        bool handled;
        try
        {
            handled = shouldHandle(new AdaptiveConcurrencyLimiterPredicateArguments<T>(frame, frame.Outcome));
        }
        catch
        {
            controller.ReleaseWithoutSample();
            throw;
        }

        ref var slot = ref frame.Slots[index];
        controller.Release(new AdaptivePermit(slot.Long, (int)slot.Double, slot.Int), handled);
        return new(false);
    }
}

/// <summary>The adaptive concurrency limiter for the non-generic builder: one controller per <c>Build()</c>, shared by every result type.</summary>
internal sealed class AdaptiveConcurrencyLimiterStrategyFactory : StrategyFactory
{
    private readonly AdaptiveConcurrencyLimiterStrategyOptions<object> _options;

    public AdaptiveConcurrencyLimiterStrategyFactory(AdaptiveConcurrencyLimiterStrategyOptions<object> options, StrategyBuildContext context)
    {
        _options = options.Snapshot();
        Controller = AdaptiveLimiterSetup.CreateController(_options, context);
    }

    public AdaptiveLimitController Controller { get; }

    public override PipelineStrategy<TResult> Create<TResult>()
    {
        if (typeof(TResult) == typeof(object))
        {
            return (PipelineStrategy<TResult>)(object)new AdaptiveConcurrencyLimiterStrategy<object>(Controller, _options.ShouldHandle, _options.OnRejected);
        }

        var shouldHandle = _options.ShouldHandle;
        return new AdaptiveConcurrencyLimiterStrategy<TResult>(
            Controller,
            ReferenceEquals(shouldHandle, AdaptiveConcurrencyLimiterStrategyOptions<object>.DefaultShouldHandle)
                ? AdaptiveConcurrencyLimiterStrategyOptions<TResult>.DefaultShouldHandle
                : args => shouldHandle(args.AsObject()),
            _options.OnRejected);
    }
}

internal static class AdaptiveLimiterSetup
{
    public static AdaptiveLimitController CreateController<TResult>(AdaptiveConcurrencyLimiterStrategyOptions<TResult> options, StrategyBuildContext context) => new(
        context.TimeProvider,
        options.Algorithm,
        options.InitialLimit,
        options.MinLimit,
        options.MaxLimit,
        options.BackoffRatio,
        options.Smoothing,
        options.Tolerance,
        options.LatencyWindow,
        options.LatencyThreshold);
}
