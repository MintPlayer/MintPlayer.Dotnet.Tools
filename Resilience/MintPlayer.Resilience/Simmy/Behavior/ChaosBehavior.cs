using System.Runtime.CompilerServices;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Simmy.Behavior;

/// <summary>Options of the behavior-injection chaos strategy: it runs a custom action before the callback.</summary>
public class ChaosBehaviorStrategyOptions : ChaosStrategyOptions
{
    /// <summary>Initializes the options.</summary>
    public ChaosBehaviorStrategyOptions() => Name = "Chaos.Behavior";

    /// <summary>Gets or sets the event raised after the injected behavior has run, before the callback runs.</summary>
    public Func<OnBehaviorInjectedArguments, ValueTask>? OnBehaviorInjected { get; set; }

    /// <summary>Gets or sets the behavior to inject (an action, so it stays asynchronous). Required.</summary>
    public Func<BehaviorGeneratorArguments, ValueTask>? BehaviorGenerator { get; set; }

    internal override void Validate()
    {
        base.Validate();
        RequireNotNull(BehaviorGenerator, nameof(BehaviorGenerator));
    }
}

/// <summary>The arguments of <see cref="ChaosBehaviorStrategyOptions.BehaviorGenerator"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct BehaviorGeneratorArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    public BehaviorGeneratorArguments(ResilienceContext context) => _source = context;

    internal BehaviorGeneratorArguments(ExecutionFrame frame) => _source = frame;

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>The arguments of <see cref="ChaosBehaviorStrategyOptions.OnBehaviorInjected"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnBehaviorInjectedArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    public OnBehaviorInjectedArguments(ResilienceContext context) => _source = context;

    internal OnBehaviorInjectedArguments(ExecutionFrame frame) => _source = frame;

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>Behavior injection for any result type; one instance per <c>Build()</c>.</summary>
internal sealed class ChaosBehaviorStrategyFactory : StrategyFactory
{
    public ChaosBehaviorStrategyFactory(ChaosBehaviorStrategyOptions options)
    {
        Settings = new ChaosSettings(options);
        Behavior = options.BehaviorGenerator!;
        OnBehaviorInjected = options.OnBehaviorInjected;
    }

    public ChaosSettings Settings { get; }

    public Func<BehaviorGeneratorArguments, ValueTask> Behavior { get; }

    public Func<OnBehaviorInjectedArguments, ValueTask>? OnBehaviorInjected { get; }

    public override PipelineStrategy<TResult> Create<TResult>() => new ChaosBehaviorStrategy<TResult>(this);
}

/// <summary>
/// Behavior injection as an enter hook: the behavior runs, then the execution continues. An exception thrown by
/// the behavior becomes the outcome (the callback does not run). No slot use.
/// </summary>
internal sealed class ChaosBehaviorStrategy<T>(ChaosBehaviorStrategyFactory shared) : PipelineStrategy<T>
{
    public override ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index)
    {
        var settings = shared.Settings;
        if (settings.IsOff)
        {
            return new(true);
        }

        var decision = settings.Decide(frame);
        return decision == ChaosDecision.Inject ? InjectAsync(frame) : ChaosSettings.Complete(frame, decision);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> InjectAsync(ExecutionFrame<T> frame)
    {
        var continueOnCapturedContext = frame.ContinueOnCapturedContext;
        await shared.Behavior(new BehaviorGeneratorArguments(frame)).ConfigureAwait(continueOnCapturedContext);
        if (shared.OnBehaviorInjected is { } onBehaviorInjected)
        {
            await onBehaviorInjected(new OnBehaviorInjectedArguments(frame)).ConfigureAwait(continueOnCapturedContext);
        }

        if (frame.CancellationToken.IsCancellationRequested)
        {
            frame.Outcome = Outcome.FromException<T>(new OperationCanceledException(frame.CancellationToken));
            return false;
        }

        return true;
    }
}
