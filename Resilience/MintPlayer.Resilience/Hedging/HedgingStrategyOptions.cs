using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Hedging;

/// <summary>Options of the hedging strategy.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>
/// Hedging runs the primary attempt and, while no acceptable outcome has arrived, up to
/// <see cref="MaxHedgedAttempts"/> hedged attempts, and returns the first outcome that
/// <see cref="ShouldHandle"/> does not handle. The strategies added after the hedging strategy run once per
/// attempt, each attempt with its own copy of the <see cref="ResilienceContext"/>.
/// </remarks>
public class HedgingStrategyOptions<TResult> : ResilienceStrategyOptions
{
    internal static readonly Func<HedgingPredicateArguments<TResult>, bool> DefaultShouldHandle = static args => DefaultPredicates.HandleOutcome(args.Outcome);

    /// <summary>
    /// The default <see cref="ActionGenerator"/>: every hedged attempt runs the rest of the pipeline and the
    /// original callback again, on the attempt's context. A synchronous execution (<c>Execute</c>) runs it on
    /// the thread pool so that attempts can overlap.
    /// </summary>
    internal static readonly Func<HedgingActionGeneratorArguments<TResult>, Func<ValueTask<Outcome<TResult>>>?> DefaultActionGenerator = static args => () =>
    {
        if (args.PrimaryContext.IsSynchronous)
        {
            return new(Task.Run(() => args.Callback(args.ActionContext).AsTask()));
        }

        return args.Callback(args.ActionContext);
    };

    /// <summary>Initializes the options with Polly's defaults: 1 hedged attempt after 2 s.</summary>
    public HedgingStrategyOptions() => Name = "Hedging";

    /// <summary>
    /// Gets or sets how long to wait for an outcome before starting the next hedged attempt. Default 2 s.
    /// <see cref="TimeSpan.Zero"/> starts every attempt at once (parallel mode). A negative value, such as
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, starts the next attempt only when the previous
    /// ones completed with a handled outcome (fallback mode). Ignored when <see cref="DelayGenerator"/> is set.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the maximum number of hedged attempts, the primary attempt not included. Default 1; valid 1 to 10.</summary>
    public int MaxHedgedAttempts { get; set; } = 1;

    /// <summary>
    /// Gets or sets the synchronous predicate that decides whether an attempt's outcome is handled, i.e. not
    /// acceptable, so hedging goes on. Default: every exception except <see cref="OperationCanceledException"/>,
    /// and every rejection. A <see cref="PredicateBuilder{TResult}"/> converts to it implicitly.
    /// </summary>
    public Func<HedgingPredicateArguments<TResult>, bool> ShouldHandle { get; set; } = DefaultShouldHandle;

    /// <summary>
    /// Gets or sets the generator of hedged actions. It is called for each hedged attempt (not for the
    /// primary one) and returns the action to run, or <see langword="null"/> to start no attempt this time.
    /// The default runs the rest of the pipeline again with <see cref="HedgingActionGeneratorArguments{TResult}.Callback"/>.
    /// </summary>
    public Func<HedgingActionGeneratorArguments<TResult>, Func<ValueTask<Outcome<TResult>>>?> ActionGenerator { get; set; } = DefaultActionGenerator;

    /// <summary>
    /// Gets or sets a synchronous generator of the delay before each hedged attempt; it overrides
    /// <see cref="Delay"/> and has the same special values.
    /// </summary>
    public Func<HedgingDelayGeneratorArguments, TimeSpan>? DelayGenerator { get; set; }

    /// <summary>Gets or sets the event raised before each hedged attempt starts.</summary>
    public Func<OnHedgingArguments<TResult>, ValueTask>? OnHedging { get; set; }

    internal override void Validate()
    {
        if (MaxHedgedAttempts is < 1 or > 10)
        {
            Invalid($"The field {nameof(MaxHedgedAttempts)} must be between 1 and 10.");
        }

        RequireNotNull(ShouldHandle, nameof(ShouldHandle));
        RequireNotNull(ActionGenerator, nameof(ActionGenerator));
    }

    /// <summary>A shallow copy, taken at <c>Build()</c> so later changes to the options do not leak into a built pipeline.</summary>
    internal HedgingStrategyOptions<TResult> Snapshot() => (HedgingStrategyOptions<TResult>)MemberwiseClone();
}

/// <summary>The arguments of <see cref="HedgingStrategyOptions{TResult}.ShouldHandle"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
public readonly struct HedgingPredicateArguments<TResult>
{
    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the attempt.</param>
    /// <param name="outcome">The outcome of the attempt.</param>
    public HedgingPredicateArguments(ResilienceContext context, Outcome<TResult> outcome)
    {
        Context = context;
        Outcome = outcome;
    }

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the attempt.</param>
    /// <param name="outcome">The outcome of the attempt.</param>
    /// <param name="attemptNumber">The 0-based number of the attempt (0 is the primary attempt).</param>
    public HedgingPredicateArguments(ResilienceContext context, Outcome<TResult> outcome, int attemptNumber)
        : this(context, outcome) => AttemptNumber = attemptNumber;

    /// <summary>Gets the outcome of the attempt.</summary>
    public Outcome<TResult> Outcome { get; }

    /// <summary>Gets the context of the attempt (its own copy of the primary context).</summary>
    public ResilienceContext Context { get; }

    /// <summary>Gets the 0-based number of the attempt (0 is the primary attempt), when known.</summary>
    public int? AttemptNumber { get; }
}

/// <summary>The arguments of <see cref="HedgingStrategyOptions{TResult}.ActionGenerator"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>
/// <see cref="PrimaryContext"/> is the context the hedging strategy received. Every attempt runs on its own
/// <see cref="ActionContext"/>, a copy of the primary context, so concurrent attempts do not race on it.
/// </remarks>
public readonly struct HedgingActionGeneratorArguments<TResult>
{
    /// <summary>Initializes the arguments.</summary>
    /// <param name="primaryContext">The context the hedging strategy received.</param>
    /// <param name="actionContext">The context of this hedged attempt.</param>
    /// <param name="attemptNumber">The 0-based number of the attempt (the first hedged attempt is 1).</param>
    /// <param name="callback">Runs the rest of the pipeline and the original callback on a given context.</param>
    public HedgingActionGeneratorArguments(ResilienceContext primaryContext, ResilienceContext actionContext, int attemptNumber, Func<ResilienceContext, ValueTask<Outcome<TResult>>> callback)
    {
        PrimaryContext = primaryContext;
        ActionContext = actionContext;
        AttemptNumber = attemptNumber;
        Callback = callback;
    }

    /// <summary>Gets the context the hedging strategy received.</summary>
    public ResilienceContext PrimaryContext { get; }

    /// <summary>Gets the context of this hedged attempt, a copy of <see cref="PrimaryContext"/>.</summary>
    public ResilienceContext ActionContext { get; }

    /// <summary>Gets the 0-based number of the attempt (the first hedged attempt is 1).</summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// Gets the callback that runs the rest of the pipeline (the strategies after the hedging strategy) and the
    /// original callback on the given context. Its task is pooled: await it exactly once, before the hedging
    /// execution completes.
    /// </summary>
    public Func<ResilienceContext, ValueTask<Outcome<TResult>>> Callback { get; }
}

/// <summary>The arguments of <see cref="HedgingStrategyOptions{TResult}.DelayGenerator"/>.</summary>
public readonly struct HedgingDelayGeneratorArguments
{
    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context the hedging strategy received.</param>
    /// <param name="attemptNumber">The number of attempts started so far, i.e. the 0-based number of the next attempt.</param>
    public HedgingDelayGeneratorArguments(ResilienceContext context, int attemptNumber)
    {
        Context = context;
        AttemptNumber = attemptNumber;
    }

    /// <summary>Gets the context the hedging strategy received.</summary>
    public ResilienceContext Context { get; }

    /// <summary>Gets the number of attempts started so far, i.e. the 0-based number of the next attempt.</summary>
    public int AttemptNumber { get; }
}

/// <summary>The arguments of <see cref="HedgingStrategyOptions{TResult}.OnHedging"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
public readonly struct OnHedgingArguments<TResult>
{
    /// <summary>Initializes the arguments.</summary>
    /// <param name="primaryContext">The context the hedging strategy received.</param>
    /// <param name="actionContext">The context of the hedged attempt that is starting.</param>
    /// <param name="attemptNumber">The 0-based number of the hedged attempt (the first hedged attempt is 0), as in Polly.</param>
    public OnHedgingArguments(ResilienceContext primaryContext, ResilienceContext actionContext, int attemptNumber)
    {
        PrimaryContext = primaryContext;
        ActionContext = actionContext;
        AttemptNumber = attemptNumber;
    }

    /// <summary>Gets the context the hedging strategy received.</summary>
    public ResilienceContext PrimaryContext { get; }

    /// <summary>Gets the context of the hedged attempt that is starting.</summary>
    public ResilienceContext ActionContext { get; }

    /// <summary>Gets the 0-based number of the hedged attempt (the first hedged attempt is 0), as in Polly.</summary>
    public int AttemptNumber { get; }
}
