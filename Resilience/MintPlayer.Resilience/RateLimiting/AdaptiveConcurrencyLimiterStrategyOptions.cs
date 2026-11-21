using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.RateLimiting;

/// <summary>How an adaptive concurrency limiter moves its limit (Netflix concurrency-limits).</summary>
public enum AdaptiveConcurrencyAlgorithm
{
    /// <summary>
    /// Additive increase, multiplicative decrease: every call that finishes without a drop while the limiter
    /// is at least half used adds <c>1 / limit</c> (so about +1 per round trip); a drop multiplies the
    /// limit by <see cref="AdaptiveConcurrencyLimiterStrategyOptions{TResult}.BackoffRatio"/>, once per
    /// burst (the calls admitted before a backoff cannot trigger another). Reacts only to drops: handled
    /// outcomes and calls slower than
    /// <see cref="AdaptiveConcurrencyLimiterStrategyOptions{TResult}.LatencyThreshold"/>, which is what
    /// makes AIMD latency-aware.
    /// </summary>
    Aimd,

    /// <summary>
    /// Netflix's gradient: the limit follows the ratio of the no-load latency (the lowest latency seen) to
    /// the latest latency. <c>gradient = clamp(Tolerance × noLoadLatency / latency, 0.5, 1)</c> and
    /// <c>target = limit × gradient + √limit</c>; each sample moves the limit <c>Smoothing / limit</c> of the
    /// way to the target, so about <see cref="AdaptiveConcurrencyLimiterStrategyOptions{TResult}.Smoothing"/>
    /// per round trip. While latency stays within <c>Tolerance</c> of the no-load latency the limit grows by
    /// about <c>Smoothing × √limit</c> per round trip; beyond it the limit shrinks, and settles where the
    /// queueing it causes balances the √limit headroom. A drop also backs off by
    /// <see cref="AdaptiveConcurrencyLimiterStrategyOptions{TResult}.BackoffRatio"/>, once per burst.
    /// </summary>
    Gradient,
}

/// <summary>
/// Options of the adaptive concurrency limiter (beyond Polly, Netflix concurrency-limits style): a
/// concurrency limit that adjusts itself from the observed latency and failures of the calls, instead of
/// a fixed number. A call that finds the limit reached is rejected at once
/// (<see cref="RejectionKind.RateLimited"/>, no <c>RetryAfter</c>, 0 B).
/// </summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>
/// <para>
/// Each finished call is a sample: its latency (admission to outcome) and whether it was a drop, i.e.
/// <see cref="ShouldHandle"/> matched or it took longer than <see cref="LatencyThreshold"/>. The limit
/// stays within [<see cref="MinLimit"/>, <see cref="MaxLimit"/>] and does not grow while fewer than half
/// of it is in use (an idle service would otherwise inflate it without evidence).
/// </para>
/// <para>
/// Everything is lock-free: admission is a compare-and-swap on the in-flight count, and a sample updates
/// the limit (a <see cref="double"/>) by compare-and-swap. Under concurrency the no-load latency and the
/// limit are updated one after the other, not atomically together, which only blurs the estimate. The
/// state belongs to the built pipeline and is shared by all its result types.
/// </para>
/// </remarks>
public class AdaptiveConcurrencyLimiterStrategyOptions<TResult> : ResilienceStrategyOptions
{
    internal static readonly Func<AdaptiveConcurrencyLimiterPredicateArguments<TResult>, bool> DefaultShouldHandle = static args => DefaultPredicates.HandleOutcome(args.Outcome);

    /// <summary>Initializes the options: gradient algorithm, limit 20 within 1–1000.</summary>
    public AdaptiveConcurrencyLimiterStrategyOptions() => Name = "AdaptiveConcurrencyLimiter";

    /// <summary>Gets or sets the algorithm. Default <see cref="AdaptiveConcurrencyAlgorithm.Gradient"/>.</summary>
    public AdaptiveConcurrencyAlgorithm Algorithm { get; set; } = AdaptiveConcurrencyAlgorithm.Gradient;

    /// <summary>
    /// Gets or sets the limit to start from. Default 20; valid <see cref="MinLimit"/> to <see cref="MaxLimit"/>.
    /// With the gradient algorithm the first samples set the no-load latency, so start from a limit the
    /// service handles without queueing; a start deep in overload learns the overloaded latency as its
    /// baseline (see <see cref="LatencyWindow"/>).
    /// </summary>
    public int InitialLimit { get; set; } = 20;

    /// <summary>Gets or sets the lowest the limit may go. Default 1; valid 1 to <see cref="MaxLimit"/>.</summary>
    public int MinLimit { get; set; } = 1;

    /// <summary>Gets or sets the highest the limit may go. Default 1000; valid <see cref="MinLimit"/> to <see cref="int.MaxValue"/>.</summary>
    public int MaxLimit { get; set; } = 1000;

    /// <summary>Gets or sets the factor the limit is multiplied by on a drop. Default 0.9; valid 0.5 up to (not including) 1.</summary>
    public double BackoffRatio { get; set; } = 0.9;

    /// <summary>
    /// Gets or sets how far the gradient algorithm moves the limit towards its target per round trip (a
    /// limit's worth of samples). Default 0.2; valid greater than 0 up to 1.
    /// </summary>
    public double Smoothing { get; set; } = 0.2;

    /// <summary>
    /// Gets or sets how much the latency may rise above the no-load latency before the gradient algorithm
    /// shrinks the limit. Default 1.5; valid 1 to 10.
    /// </summary>
    public double Tolerance { get; set; } = 1.5;

    /// <summary>
    /// Gets or sets how slowly the gradient algorithm forgets its no-load latency: while the limit sits at
    /// its floor (max(<see cref="MinLimit"/>, 4) + 1), each sample lets the estimate rise by a factor
    /// <c>1 + 1 / LatencyWindow</c>. Default 600; valid 10 to 100 000.
    /// </summary>
    /// <remarks>
    /// Samples taken at a higher limit never raise the estimate, because their extra latency may be the
    /// queueing the limit caused; forgetting on them makes the limit creep up under a sustained overload.
    /// The price: when a service's baseline latency rises for good by a factor <c>r</c> greater than
    /// <see cref="Tolerance"/>, the gradient cannot tell it from queueing and settles at the limit
    /// <c>(1 / (1 − Tolerance / r))²</c> (16 for r = 2 with the default tolerance; at least 4) instead of
    /// re-learning.
    /// Where baselines move, prefer <see cref="AdaptiveConcurrencyAlgorithm.Aimd"/> with a
    /// <see cref="LatencyThreshold"/>.
    /// </remarks>
    public int LatencyWindow { get; set; } = 600;

    /// <summary>Gets or sets a latency above which a call counts as a drop, whatever its outcome. Default none; valid 1 ms to 1 day.</summary>
    public TimeSpan? LatencyThreshold { get; set; }

    /// <summary>
    /// Gets or sets the synchronous predicate that decides whether an outcome is a drop (overload). Default:
    /// every exception except <see cref="OperationCanceledException"/>, and every rejection from an inner
    /// strategy. A <see cref="PredicateBuilder{TResult}"/> converts to it implicitly.
    /// </summary>
    public Func<AdaptiveConcurrencyLimiterPredicateArguments<TResult>, bool> ShouldHandle { get; set; } = DefaultShouldHandle;

    /// <summary>Gets or sets the event raised when a call is refused, before the rejection is returned.</summary>
    public Func<OnLimiterRejectedArguments, ValueTask>? OnRejected { get; set; }

    internal override void Validate()
    {
        if (MinLimit < 1)
        {
            Invalid($"The field {nameof(MinLimit)} must be between 1 and {nameof(MaxLimit)}.");
        }

        if (MaxLimit < MinLimit)
        {
            Invalid($"The field {nameof(MaxLimit)} must be at least {nameof(MinLimit)}.");
        }

        if (InitialLimit < MinLimit || InitialLimit > MaxLimit)
        {
            Invalid($"The field {nameof(InitialLimit)} must be between {nameof(MinLimit)} and {nameof(MaxLimit)}.");
        }

        if (!Enum.IsDefined(Algorithm))
        {
            Invalid($"The field {nameof(Algorithm)} has an unsupported value '{Algorithm}'.");
        }

        if (!(BackoffRatio >= 0.5 && BackoffRatio < 1))
        {
            Invalid($"The field {nameof(BackoffRatio)} must be at least 0.5 and less than 1.");
        }

        if (!(Smoothing > 0 && Smoothing <= 1))
        {
            Invalid($"The field {nameof(Smoothing)} must be greater than 0 and at most 1.");
        }

        if (!(Tolerance >= 1 && Tolerance <= 10))
        {
            Invalid($"The field {nameof(Tolerance)} must be between 1 and 10.");
        }

        if (LatencyWindow is < 10 or > 100_000)
        {
            Invalid($"The field {nameof(LatencyWindow)} must be between 10 and 100000.");
        }

        if (LatencyThreshold is { } threshold)
        {
            RequireRange(threshold, TimeSpan.FromMilliseconds(1), TimeSpan.FromDays(1), nameof(LatencyThreshold));
        }

        RequireNotNull(ShouldHandle, nameof(ShouldHandle));
    }

    internal AdaptiveConcurrencyLimiterStrategyOptions<TResult> Snapshot() => (AdaptiveConcurrencyLimiterStrategyOptions<TResult>)MemberwiseClone();
}

/// <summary>Options of the adaptive concurrency limiter for a non-generic <see cref="ResiliencePipelineBuilder"/>.</summary>
public class AdaptiveConcurrencyLimiterStrategyOptions : AdaptiveConcurrencyLimiterStrategyOptions<object>
{
}

/// <summary>The arguments of <see cref="AdaptiveConcurrencyLimiterStrategyOptions{TResult}.ShouldHandle"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct AdaptiveConcurrencyLimiterPredicateArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="outcome">The outcome of the call.</param>
    public AdaptiveConcurrencyLimiterPredicateArguments(ResilienceContext context, Outcome<TResult> outcome)
        : this((object?)context, outcome)
    {
    }

    internal AdaptiveConcurrencyLimiterPredicateArguments(object? source, Outcome<TResult> outcome)
    {
        _source = source;
        Outcome = outcome;
    }

    /// <summary>Gets the outcome of the call.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    internal AdaptiveConcurrencyLimiterPredicateArguments<object> AsObject() => new(_source, Outcome.AsObjectOutcome());
}
