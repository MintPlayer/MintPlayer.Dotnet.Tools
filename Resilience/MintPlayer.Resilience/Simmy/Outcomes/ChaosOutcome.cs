using System.ComponentModel;
using System.Runtime.CompilerServices;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Simmy.Outcomes;

/// <summary>Options of the outcome-injection chaos strategy: it returns a generated outcome (a result or an exception) instead of running the callback.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
public class ChaosOutcomeStrategyOptions<TResult> : ChaosStrategyOptions
{
    /// <summary>Initializes the options.</summary>
    public ChaosOutcomeStrategyOptions() => Name = "Chaos.Outcome";

    /// <summary>Gets or sets the event raised when an outcome is injected, before the execution ends with it.</summary>
    public Func<OnOutcomeInjectedArguments<TResult>, ValueTask>? OnOutcomeInjected { get; set; }

    /// <summary>
    /// Gets or sets the synchronous generator of the outcome to inject; <see langword="null"/> injects nothing.
    /// Required. An <see cref="OutcomeGenerator{TResult}"/> converts to it implicitly.
    /// </summary>
    public Func<OutcomeGeneratorArguments, Outcome<TResult>?>? OutcomeGenerator { get; set; }

    internal override void Validate()
    {
        base.Validate();
        RequireNotNull(OutcomeGenerator, nameof(OutcomeGenerator));
    }
}

/// <summary>The arguments of <see cref="ChaosOutcomeStrategyOptions{TResult}.OutcomeGenerator"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OutcomeGeneratorArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    public OutcomeGeneratorArguments(ResilienceContext context) => _source = context;

    internal OutcomeGeneratorArguments(ExecutionFrame frame) => _source = frame;

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>The arguments of <see cref="ChaosOutcomeStrategyOptions{TResult}.OnOutcomeInjected"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnOutcomeInjectedArguments<TResult>
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="outcome">The injected outcome.</param>
    public OnOutcomeInjectedArguments(ResilienceContext context, Outcome<TResult> outcome)
        : this((object?)context, outcome)
    {
    }

    internal OnOutcomeInjectedArguments(object? source, Outcome<TResult> outcome)
    {
        _source = source;
        Outcome = outcome;
    }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets the injected outcome.</summary>
    public Outcome<TResult> Outcome { get; }
}

/// <summary>
/// Builds a <see cref="ChaosOutcomeStrategyOptions{TResult}.OutcomeGenerator"/> that picks one of several results or
/// exceptions by weight: <c>new OutcomeGenerator&lt;HttpResponseMessage&gt;().AddResult(() =&gt; new(HttpStatusCode.TooManyRequests)).AddException&lt;HttpRequestException&gt;()</c>.
/// </summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
public sealed class OutcomeGenerator<TResult>
{
    private const int DefaultWeight = 100;

    private readonly GeneratorHelper<TResult> _helper;

    /// <summary>Initializes an empty generator (it injects nothing until an outcome is added).</summary>
    public OutcomeGenerator()
        : this(Random.Shared.Next)
    {
    }

    /// <summary>For tests: <paramref name="weightGenerator"/> returns a value in [0, total weight).</summary>
    internal OutcomeGenerator(Func<int, int> weightGenerator) => _helper = new(weightGenerator);

    /// <summary>Adds an exception factory.</summary>
    /// <param name="generator">Creates the exception.</param>
    /// <param name="weight">The relative weight. Default 100.</param>
    /// <returns>This generator.</returns>
    public OutcomeGenerator<TResult> AddException(Func<Exception> generator, int weight = DefaultWeight)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _helper.AddOutcome(_ => Outcome.FromException<TResult>(generator()), weight);
        return this;
    }

    /// <summary>Adds an exception factory that reads the context.</summary>
    /// <param name="generator">Creates the exception.</param>
    /// <param name="weight">The relative weight. Default 100.</param>
    /// <returns>This generator.</returns>
    public OutcomeGenerator<TResult> AddException(Func<ResilienceContext, Exception> generator, int weight = DefaultWeight)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _helper.AddOutcome(context => Outcome.FromException<TResult>(generator(context)), weight);
        return this;
    }

    /// <summary>Adds an exception type, created with its parameterless constructor.</summary>
    /// <typeparam name="TException">The type of the exception.</typeparam>
    /// <param name="weight">The relative weight. Default 100.</param>
    /// <returns>This generator.</returns>
    public OutcomeGenerator<TResult> AddException<TException>(int weight = DefaultWeight)
        where TException : Exception, new()
    {
        _helper.AddOutcome(static _ => Outcome.FromException<TResult>(new TException()), weight);
        return this;
    }

    /// <summary>Adds a result factory.</summary>
    /// <param name="generator">Creates the result.</param>
    /// <param name="weight">The relative weight. Default 100.</param>
    /// <returns>This generator.</returns>
    public OutcomeGenerator<TResult> AddResult(Func<TResult> generator, int weight = DefaultWeight)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _helper.AddOutcome(_ => Outcome.FromResult(generator()), weight);
        return this;
    }

    /// <summary>Adds a result factory that reads the context.</summary>
    /// <param name="generator">Creates the result.</param>
    /// <param name="weight">The relative weight. Default 100.</param>
    /// <returns>This generator.</returns>
    public OutcomeGenerator<TResult> AddResult(Func<ResilienceContext, TResult> generator, int weight = DefaultWeight)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _helper.AddOutcome(context => Outcome.FromResult(generator(context)), weight);
        return this;
    }

    /// <summary>Converts the generator to a <see cref="ChaosOutcomeStrategyOptions{TResult}.OutcomeGenerator"/>.</summary>
    /// <param name="generator">The generator.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator Func<OutcomeGeneratorArguments, Outcome<TResult>?>(OutcomeGenerator<TResult> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        var generatorDelegate = generator._helper.CreateGenerator();
        return args => generatorDelegate(args.Context);
    }
}

/// <summary>Outcome injection as an enter hook: an injected outcome short-circuits the execution. No slot use.</summary>
internal sealed class ChaosOutcomeStrategy<T> : PipelineStrategy<T>
{
    private readonly ChaosSettings _settings;
    private readonly Func<OutcomeGeneratorArguments, Outcome<T>?> _outcomeGenerator;
    private readonly Func<OnOutcomeInjectedArguments<T>, ValueTask>? _onOutcomeInjected;

    public ChaosOutcomeStrategy(ChaosOutcomeStrategyOptions<T> options)
    {
        _settings = new ChaosSettings(options);
        _outcomeGenerator = options.OutcomeGenerator!;
        _onOutcomeInjected = options.OnOutcomeInjected;
    }

    public override ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index)
    {
        if (_settings.IsOff)
        {
            return new(true);
        }

        var decision = _settings.Decide(frame);
        if (decision != ChaosDecision.Inject)
        {
            return ChaosSettings.Complete(frame, decision);
        }

        if (_outcomeGenerator(new OutcomeGeneratorArguments(frame)) is not { } outcome)
        {
            return ChaosSettings.Complete(frame, ChaosSettings.Continue(frame));
        }

        if (_onOutcomeInjected is { } onOutcomeInjected)
        {
            return InjectAsync(onOutcomeInjected, frame, outcome);
        }

        frame.Outcome = outcome;
        return new(false);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<bool> InjectAsync(Func<OnOutcomeInjectedArguments<T>, ValueTask> onOutcomeInjected, ExecutionFrame<T> frame, Outcome<T> outcome)
    {
        await onOutcomeInjected(new OnOutcomeInjectedArguments<T>(frame, outcome)).ConfigureAwait(frame.ContinueOnCapturedContext);
        frame.Outcome = outcome;
        return false;
    }
}
