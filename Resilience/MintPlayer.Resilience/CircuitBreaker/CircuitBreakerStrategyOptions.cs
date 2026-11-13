using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.CircuitBreaker;

/// <summary>Options of the circuit-breaker strategy.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>
/// The breaker measures handled outcomes (failures) and the others (successes) over a rolling
/// <see cref="SamplingDuration"/>. Once at least <see cref="MinimumThroughput"/> calls were measured and
/// the failure ratio reaches <see cref="FailureRatio"/>, the circuit opens for <see cref="BreakDuration"/>
/// and rejects calls. After the break, one probe call is let through (half-open): its success closes the
/// circuit, its failure opens it again. Beyond Polly, the circuit can also break on the share of slow
/// calls; see <see cref="SlowCallDurationThreshold"/>.
/// </remarks>
public class CircuitBreakerStrategyOptions<TResult> : ResilienceStrategyOptions
{
    internal static readonly Func<CircuitBreakerPredicateArguments<TResult>, bool> DefaultShouldHandle = static args => DefaultPredicates.HandleOutcome(args.Outcome);

    /// <summary>Initializes the options with Polly's defaults: ratio 0.1, minimum throughput 100, sampling 30 s, break 5 s.</summary>
    public CircuitBreakerStrategyOptions() => Name = "CircuitBreaker";

    /// <summary>
    /// Gets or sets the failure ratio (failures / measured calls in the sampling window) at or above which
    /// the circuit opens. Default 0.1; valid 0 to 1.
    /// </summary>
    public double FailureRatio { get; set; } = 0.1;

    /// <summary>Gets or sets how many calls the sampling window must hold before the circuit may open. Default 100; valid 2 to <see cref="int.MaxValue"/>.</summary>
    public int MinimumThroughput { get; set; } = 100;

    /// <summary>
    /// Gets or sets the length of the rolling health window. It is kept as 10 windows anchored at the first
    /// event in each, exactly as Polly does. Default 30 s; valid 0.5 s to 1 day.
    /// </summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how long the circuit stays open before a probe is allowed. Default 5 s; valid 0.5 s to 1 day.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets a synchronous generator that decides the break duration each time the circuit opens
    /// on its own (not on manual isolation), overriding <see cref="BreakDuration"/>. A negative value
    /// means a zero break. The half-open window stays <see cref="BreakDuration"/>, as in Polly.
    /// </summary>
    /// <remarks>
    /// <para>Polly's generator returns <c>ValueTask&lt;TimeSpan&gt;</c> and is awaited synchronously; this one is synchronous.</para>
    /// <para>
    /// The failure rate and count passed in are those of the window at the moment the circuit broke from
    /// closed. When a failed probe re-opens the circuit, the same values are passed again: the window is
    /// cleared when the probe is admitted (see the README, "Differences from Polly").
    /// </para>
    /// <para>
    /// When two calls race to open the circuit, the generator may run for both; only one result is used.
    /// </para>
    /// </remarks>
    public Func<BreakDurationGeneratorArguments, TimeSpan>? BreakDurationGenerator { get; set; }

    /// <summary>
    /// Gets or sets the synchronous predicate that decides whether an outcome is a failure. Default: every
    /// exception except <see cref="OperationCanceledException"/>, and every rejection. A
    /// <see cref="PredicateBuilder{TResult}"/> converts to it implicitly.
    /// </summary>
    public Func<CircuitBreakerPredicateArguments<TResult>, bool> ShouldHandle { get; set; } = DefaultShouldHandle;

    /// <summary>Gets or sets the event raised when the circuit closes. The call that closed it awaits the event.</summary>
    public Func<OnCircuitClosedArguments<TResult>, ValueTask>? OnClosed { get; set; }

    /// <summary>Gets or sets the event raised when the circuit opens or is isolated. The call that opened it awaits the event.</summary>
    public Func<OnCircuitOpenedArguments<TResult>, ValueTask>? OnOpened { get; set; }

    /// <summary>Gets or sets the event raised when the circuit goes half-open. The probe call awaits it before it runs.</summary>
    public Func<OnCircuitHalfOpenedArguments, ValueTask>? OnHalfOpened { get; set; }

    /// <summary>Gets or sets a control that isolates and closes the circuit manually. One control may drive several breakers.</summary>
    public CircuitBreakerManualControl? ManualControl { get; set; }

    /// <summary>Gets or sets a provider that reports the circuit state. A provider serves one breaker only.</summary>
    public CircuitBreakerStateProvider? StateProvider { get; set; }

    /// <summary>
    /// Gets or sets the duration at or above which a call counts as slow (beyond Polly, as resilience4j's
    /// <c>slowCallDurationThreshold</c>). Default <see langword="null"/>: slow calls are not tracked. Valid
    /// 1 ms to 1 day.
    /// </summary>
    /// <remarks>
    /// When set, the duration of every call is measured (from admission to outcome, excluding the
    /// half-open event), and the circuit also opens when the share of slow calls in the sampling window
    /// reaches <see cref="SlowCallRatio"/>, behind the same <see cref="MinimumThroughput"/>. Slow calls
    /// count whether they succeed or fail. A slow probe counts as a failed probe and re-opens the circuit.
    /// </remarks>
    public TimeSpan? SlowCallDurationThreshold { get; set; }

    /// <summary>
    /// Gets or sets the share of slow calls (slow / measured calls in the sampling window) at or above which
    /// the circuit opens; used only with <see cref="SlowCallDurationThreshold"/>. Default 1.0 (every
    /// measured call slow); valid greater than 0 up to 1.
    /// </summary>
    public double SlowCallRatio { get; set; } = 1.0;

    internal override void Validate()
    {
        if (!(FailureRatio >= 0 && FailureRatio <= 1))
        {
            Invalid($"The field {nameof(FailureRatio)} must be between 0 and 1.");
        }

        if (MinimumThroughput < 2)
        {
            Invalid($"The field {nameof(MinimumThroughput)} must be between 2 and {int.MaxValue}.");
        }

        RequireRange(SamplingDuration, TimeSpan.FromMilliseconds(500), TimeSpan.FromDays(1), nameof(SamplingDuration));
        RequireRange(BreakDuration, TimeSpan.FromMilliseconds(500), TimeSpan.FromDays(1), nameof(BreakDuration));
        RequireNotNull(ShouldHandle, nameof(ShouldHandle));

        if (SlowCallDurationThreshold is { } threshold)
        {
            RequireRange(threshold, TimeSpan.FromMilliseconds(1), TimeSpan.FromDays(1), nameof(SlowCallDurationThreshold));
        }

        if (!(SlowCallRatio > 0 && SlowCallRatio <= 1))
        {
            Invalid($"The field {nameof(SlowCallRatio)} must be greater than 0 and at most 1.");
        }
    }

    /// <summary>A shallow copy, taken at <c>Build()</c> so later changes to the options do not leak into a built pipeline.</summary>
    internal CircuitBreakerStrategyOptions<TResult> Snapshot() => (CircuitBreakerStrategyOptions<TResult>)MemberwiseClone();
}

/// <summary>Options of the circuit-breaker strategy for a non-generic <see cref="ResiliencePipelineBuilder"/>.</summary>
public class CircuitBreakerStrategyOptions : CircuitBreakerStrategyOptions<object>
{
}
