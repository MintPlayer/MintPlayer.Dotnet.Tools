using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Simmy;

/// <summary>
/// Options shared by the chaos (fault-injection) strategies: whether they are enabled, and how often they inject.
/// </summary>
/// <remarks>
/// As in Polly 8 (Simmy), the chaos strategies are ordinary strategies, so they can sit in a production pipeline
/// behind a flag. A strategy that is disabled (<see cref="Enabled"/> false and no <see cref="EnabledGenerator"/>)
/// or can never inject (<see cref="InjectionRate"/> 0 and no <see cref="InjectionRateGenerator"/>) costs one branch
/// per execution and allocates nothing.
/// </remarks>
public abstract class ChaosStrategyOptions : ResilienceStrategyOptions
{
    private static readonly Func<double> DefaultRandomizer = Random.Shared.NextDouble;

    /// <summary>Gets or sets the probability of an injection, per execution. Default 0.001; valid 0 to 1. Ignored when <see cref="InjectionRateGenerator"/> is set.</summary>
    public double InjectionRate { get; set; } = 0.001;

    /// <summary>
    /// Gets or sets a synchronous generator of the injection rate, called per execution while the strategy is
    /// enabled; it overrides <see cref="InjectionRate"/>. Values outside 0 to 1 are clamped.
    /// </summary>
    public Func<InjectionRateGeneratorArguments, double>? InjectionRateGenerator { get; set; }

    /// <summary>Gets or sets a synchronous predicate, called per execution, that decides whether the strategy is enabled; it overrides <see cref="Enabled"/>.</summary>
    public Func<EnabledGeneratorArguments, bool>? EnabledGenerator { get; set; }

    /// <summary>Gets or sets whether the strategy is enabled. Default <see langword="true"/>. Ignored when <see cref="EnabledGenerator"/> is set.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the source of random numbers in [0, 1): a call is injected when it returns less than the injection rate.</summary>
    public Func<double> Randomizer { get; set; } = DefaultRandomizer;

    internal override void Validate()
    {
        if (!(InjectionRate >= 0 && InjectionRate <= 1))
        {
            Invalid($"The field {nameof(InjectionRate)} must be between 0 and 1.");
        }

        RequireNotNull(Randomizer, nameof(Randomizer));
    }
}

/// <summary>The arguments of <see cref="ChaosStrategyOptions.EnabledGenerator"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct EnabledGeneratorArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    public EnabledGeneratorArguments(ResilienceContext context) => _source = context;

    internal EnabledGeneratorArguments(ExecutionFrame frame) => _source = frame;

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>The arguments of <see cref="ChaosStrategyOptions.InjectionRateGenerator"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct InjectionRateGeneratorArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    public InjectionRateGeneratorArguments(ResilienceContext context) => _source = context;

    internal InjectionRateGeneratorArguments(ExecutionFrame frame) => _source = frame;

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>What a chaos strategy does for one execution.</summary>
internal enum ChaosDecision
{
    /// <summary>Do not inject; continue inward.</summary>
    Skip,

    /// <summary>Inject.</summary>
    Inject,

    /// <summary>The token was cancelled: the execution ends with an <see cref="OperationCanceledException"/>.</summary>
    Cancelled,
}

/// <summary>The injection decision shared by every chaos strategy (Polly's <c>ChaosStrategyHelper.ShouldInjectAsync</c>).</summary>
internal sealed class ChaosSettings
{
    private readonly bool _enabled;
    private readonly Func<EnabledGeneratorArguments, bool>? _enabledGenerator;
    private readonly double _injectionRate;
    private readonly Func<InjectionRateGeneratorArguments, double>? _injectionRateGenerator;
    private readonly Func<double> _randomizer;

    public ChaosSettings(ChaosStrategyOptions options)
    {
        _enabled = options.Enabled;
        _enabledGenerator = options.EnabledGenerator;
        _injectionRate = options.InjectionRate;
        _injectionRateGenerator = options.InjectionRateGenerator;
        _randomizer = options.Randomizer;
        IsOff = _enabledGenerator is null && (!_enabled || (_injectionRateGenerator is null && _injectionRate <= 0));
    }

    /// <summary>The strategy can never inject: its hook is a single branch.</summary>
    public bool IsOff { get; }

    /// <summary>
    /// As Polly: the token is checked before the enabled generator, before the rate generator, and before the
    /// execution continues; the call is injected when the randomizer returns less than the (clamped) rate.
    /// </summary>
    public ChaosDecision Decide(ExecutionFrame frame)
    {
        if (frame.CancellationToken.IsCancellationRequested)
        {
            return ChaosDecision.Cancelled;
        }

        var enabled = _enabledGenerator is { } enabledGenerator ? enabledGenerator(new EnabledGeneratorArguments(frame)) : _enabled;
        if (!enabled || frame.CancellationToken.IsCancellationRequested)
        {
            return Continue(frame);
        }

        var rate = _injectionRateGenerator is { } rateGenerator ? rateGenerator(new InjectionRateGeneratorArguments(frame)) : _injectionRate;
        if (frame.CancellationToken.IsCancellationRequested)
        {
            return ChaosDecision.Cancelled;
        }

        rate = rate < 0 ? 0 : rate > 1 ? 1 : rate;
        return _randomizer() < rate ? ChaosDecision.Inject : ChaosDecision.Skip;
    }

    /// <summary>Continue inward, unless the token was cancelled meanwhile.</summary>
    public static ChaosDecision Continue(ExecutionFrame frame)
        => frame.CancellationToken.IsCancellationRequested ? ChaosDecision.Cancelled : ChaosDecision.Skip;

    /// <summary>Ends the execution with the cancellation (the hook returns false).</summary>
    public static ValueTask<bool> Cancel<T>(ExecutionFrame<T> frame)
    {
        frame.Outcome = Outcome.FromException<T>(new OperationCanceledException(frame.CancellationToken));
        return new(false);
    }

    /// <summary>Maps a decision that does not inject to the hook's result.</summary>
    public static ValueTask<bool> Complete<T>(ExecutionFrame<T> frame, ChaosDecision decision)
        => decision == ChaosDecision.Cancelled ? Cancel(frame) : new(true);
}

/// <summary>Picks one of several weighted outcomes (Polly's <c>GeneratorHelper</c>), for <see cref="Fault.FaultGenerator"/> and <see cref="Outcomes.OutcomeGenerator{TResult}"/>.</summary>
internal sealed class GeneratorHelper<TResult>(Func<int, int> weightGenerator)
{
    private readonly List<int> _weights = [];
    private readonly List<Func<ResilienceContext, Outcome<TResult>>> _factories = [];
    private int _totalWeight;

    public void AddOutcome(Func<ResilienceContext, Outcome<TResult>> factory, int weight)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfNegative(weight);
        _totalWeight += weight;
        _factories.Add(factory);
        _weights.Add(weight);
    }

    /// <summary>A generator over a snapshot of the outcomes added so far; it returns null when there are none.</summary>
    public Func<ResilienceContext, Outcome<TResult>?> CreateGenerator()
    {
        if (_factories.Count == 0)
        {
            return static _ => null;
        }

        var totalWeight = _totalWeight;
        var factories = _factories.ToArray();
        var weights = _weights.ToArray();
        var generator = weightGenerator;
        return context =>
        {
            var generatedWeight = generator(totalWeight);
            var weight = 0;
            for (var i = 0; i < factories.Length; i++)
            {
                weight += weights[i];
                if (generatedWeight < weight)
                {
                    return factories[i](context);
                }
            }

            return null;
        };
    }
}
