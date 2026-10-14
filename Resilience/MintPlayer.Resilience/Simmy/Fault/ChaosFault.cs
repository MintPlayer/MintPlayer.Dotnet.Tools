using System.ComponentModel;
using System.Runtime.CompilerServices;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Simmy.Fault;

/// <summary>Options of the fault-injection chaos strategy: it throws a generated exception instead of running the callback.</summary>
public class ChaosFaultStrategyOptions : ChaosStrategyOptions
{
    /// <summary>Initializes the options.</summary>
    public ChaosFaultStrategyOptions() => Name = "Chaos.Fault";

    /// <summary>Gets or sets the event raised when a fault is injected, before the execution ends with it.</summary>
    public Func<OnFaultInjectedArguments, ValueTask>? OnFaultInjected { get; set; }

    /// <summary>
    /// Gets or sets the synchronous generator of the fault to inject; <see langword="null"/> injects nothing.
    /// Required. A <see cref="Fault.FaultGenerator"/> converts to it implicitly.
    /// </summary>
    public Func<FaultGeneratorArguments, Exception?>? FaultGenerator { get; set; }

    internal override void Validate()
    {
        base.Validate();
        RequireNotNull(FaultGenerator, nameof(FaultGenerator));
    }
}

/// <summary>The arguments of <see cref="ChaosFaultStrategyOptions.FaultGenerator"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct FaultGeneratorArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    public FaultGeneratorArguments(ResilienceContext context) => _source = context;

    internal FaultGeneratorArguments(ExecutionFrame frame) => _source = frame;

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>The arguments of <see cref="ChaosFaultStrategyOptions.OnFaultInjected"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnFaultInjectedArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="fault">The injected fault.</param>
    public OnFaultInjectedArguments(ResilienceContext context, Exception fault)
        : this((object?)context, fault)
    {
    }

    internal OnFaultInjectedArguments(object? source, Exception fault)
    {
        _source = source;
        Fault = fault;
    }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets the injected fault.</summary>
    public Exception Fault { get; }
}

/// <summary>
/// Builds a <see cref="ChaosFaultStrategyOptions.FaultGenerator"/> that picks one of several exceptions by weight:
/// <c>FaultGenerator = new FaultGenerator().AddException&lt;TimeoutException&gt;(70).AddException&lt;IOException&gt;(30)</c>.
/// </summary>
public sealed class FaultGenerator
{
    private const int DefaultWeight = 100;

    private readonly GeneratorHelper<VoidResult> _helper;

    /// <summary>Initializes an empty generator (it injects nothing until an exception is added).</summary>
    public FaultGenerator()
        : this(Random.Shared.Next)
    {
    }

    /// <summary>For tests: <paramref name="weightGenerator"/> returns a value in [0, total weight).</summary>
    internal FaultGenerator(Func<int, int> weightGenerator) => _helper = new(weightGenerator);

    /// <summary>Adds an exception factory.</summary>
    /// <param name="generator">Creates the exception.</param>
    /// <param name="weight">The relative weight. Default 100.</param>
    /// <returns>This generator.</returns>
    public FaultGenerator AddException(Func<Exception> generator, int weight = DefaultWeight)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _helper.AddOutcome(_ => new Outcome<VoidResult>(generator()), weight);
        return this;
    }

    /// <summary>Adds an exception factory that reads the context.</summary>
    /// <param name="generator">Creates the exception.</param>
    /// <param name="weight">The relative weight. Default 100.</param>
    /// <returns>This generator.</returns>
    public FaultGenerator AddException(Func<ResilienceContext, Exception> generator, int weight = DefaultWeight)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _helper.AddOutcome(context => new Outcome<VoidResult>(generator(context)), weight);
        return this;
    }

    /// <summary>Adds an exception type, created with its parameterless constructor.</summary>
    /// <typeparam name="TException">The type of the exception.</typeparam>
    /// <param name="weight">The relative weight. Default 100.</param>
    /// <returns>This generator.</returns>
    public FaultGenerator AddException<TException>(int weight = DefaultWeight)
        where TException : Exception, new()
    {
        _helper.AddOutcome(static _ => new Outcome<VoidResult>(new TException()), weight);
        return this;
    }

    /// <summary>Converts the generator to a <see cref="ChaosFaultStrategyOptions.FaultGenerator"/>.</summary>
    /// <param name="generator">The generator.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator Func<FaultGeneratorArguments, Exception?>(FaultGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        var generatorDelegate = generator._helper.CreateGenerator();
        return args => generatorDelegate(args.Context)?.RawException;
    }
}

/// <summary>Fault injection for any result type; one instance per <c>Build()</c>.</summary>
internal sealed class ChaosFaultStrategyFactory : StrategyFactory
{
    public ChaosFaultStrategyFactory(ChaosFaultStrategyOptions options)
    {
        Settings = new ChaosSettings(options);
        FaultGenerator = options.FaultGenerator!;
        OnFaultInjected = options.OnFaultInjected;
    }

    public ChaosSettings Settings { get; }

    public Func<FaultGeneratorArguments, Exception?> FaultGenerator { get; }

    public Func<OnFaultInjectedArguments, ValueTask>? OnFaultInjected { get; }

    public override PipelineStrategy<TResult> Create<TResult>() => new ChaosFaultStrategy<TResult>(this);
}

/// <summary>Fault injection as an enter hook: an injected fault short-circuits with the exception as the outcome. No slot use.</summary>
internal sealed class ChaosFaultStrategy<T>(ChaosFaultStrategyFactory shared) : PipelineStrategy<T>
{
    public override ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index)
    {
        var settings = shared.Settings;
        if (settings.IsOff)
        {
            return new(true);
        }

        var decision = settings.Decide(frame);
        if (decision != ChaosDecision.Inject)
        {
            return ChaosSettings.Complete(frame, decision);
        }

        if (shared.FaultGenerator(new FaultGeneratorArguments(frame)) is not { } fault)
        {
            return ChaosSettings.Complete(frame, ChaosSettings.Continue(frame));
        }

        if (shared.OnFaultInjected is { } onFaultInjected)
        {
            return InjectAsync(onFaultInjected, frame, fault);
        }

        frame.Outcome = Outcome.FromException<T>(fault);
        return new(false);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<bool> InjectAsync(Func<OnFaultInjectedArguments, ValueTask> onFaultInjected, ExecutionFrame<T> frame, Exception fault)
    {
        await onFaultInjected(new OnFaultInjectedArguments(frame, fault)).ConfigureAwait(frame.ContinueOnCapturedContext);
        frame.Outcome = Outcome.FromException<T>(fault);
        return false;
    }
}
