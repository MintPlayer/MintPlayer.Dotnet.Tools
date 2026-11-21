using System.Runtime.CompilerServices;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Simmy.Latency;

/// <summary>Options of the latency-injection chaos strategy: it waits before running the callback.</summary>
public class ChaosLatencyStrategyOptions : ChaosStrategyOptions
{
    /// <summary>Initializes the options.</summary>
    public ChaosLatencyStrategyOptions() => Name = "Chaos.Latency";

    /// <summary>Gets or sets the event raised after an injected delay has elapsed, before the callback runs.</summary>
    public Func<OnLatencyInjectedArguments, ValueTask>? OnLatencyInjected { get; set; }

    /// <summary>
    /// Gets or sets a synchronous generator of the delay to inject; it overrides <see cref="Latency"/>. Zero or a
    /// negative value injects nothing.
    /// </summary>
    public Func<LatencyGeneratorArguments, TimeSpan>? LatencyGenerator { get; set; }

    /// <summary>Gets or sets the delay to inject. Default 30 s. Ignored when <see cref="LatencyGenerator"/> is set.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>The arguments of <see cref="ChaosLatencyStrategyOptions.LatencyGenerator"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct LatencyGeneratorArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    public LatencyGeneratorArguments(ResilienceContext context) => _source = context;

    internal LatencyGeneratorArguments(ExecutionFrame frame) => _source = frame;

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>The arguments of <see cref="ChaosLatencyStrategyOptions.OnLatencyInjected"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnLatencyInjectedArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="latency">The injected delay.</param>
    public OnLatencyInjectedArguments(ResilienceContext context, TimeSpan latency)
        : this((object?)context, latency)
    {
    }

    internal OnLatencyInjectedArguments(object? source, TimeSpan latency)
    {
        _source = source;
        Latency = latency;
    }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets the injected delay.</summary>
    public TimeSpan Latency { get; }
}

/// <summary>Latency injection for any result type; one instance per <c>Build()</c>.</summary>
internal sealed class ChaosLatencyStrategyFactory : StrategyFactory
{
    public ChaosLatencyStrategyFactory(ChaosLatencyStrategyOptions options, StrategyBuildContext context)
    {
        Settings = new ChaosSettings(options);
        Latency = options.Latency;
        LatencyGenerator = options.LatencyGenerator;
        OnLatencyInjected = options.OnLatencyInjected;
        TimeProvider = context.TimeProvider;
    }

    public ChaosSettings Settings { get; }

    public TimeSpan Latency { get; }

    public Func<LatencyGeneratorArguments, TimeSpan>? LatencyGenerator { get; }

    public Func<OnLatencyInjectedArguments, ValueTask>? OnLatencyInjected { get; }

    public TimeProvider TimeProvider { get; }

    public override PipelineStrategy<TResult> Create<TResult>() => new ChaosLatencyStrategy<TResult>(this);
}

/// <summary>
/// Latency injection as an enter hook: an injected delay is awaited on the pipeline's <see cref="System.TimeProvider"/>
/// (blocking for <c>Execute</c>), then the execution continues. A cancellation during the delay ends the execution
/// with the <see cref="OperationCanceledException"/>. No slot use.
/// </summary>
internal sealed class ChaosLatencyStrategy<T>(ChaosLatencyStrategyFactory shared) : PipelineStrategy<T>
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

        var latency = shared.LatencyGenerator is { } generator ? generator(new LatencyGeneratorArguments(frame)) : shared.Latency;
        return latency > TimeSpan.Zero
            ? InjectAsync(frame, latency)
            : ChaosSettings.Complete(frame, ChaosSettings.Continue(frame));
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> InjectAsync(ExecutionFrame<T> frame, TimeSpan latency)
    {
        var continueOnCapturedContext = frame.ContinueOnCapturedContext;
        await DelayHelper.DelayAsync(shared.TimeProvider, latency, frame).ConfigureAwait(continueOnCapturedContext);
        if (shared.OnLatencyInjected is { } onLatencyInjected)
        {
            await onLatencyInjected(new OnLatencyInjectedArguments(frame, latency)).ConfigureAwait(continueOnCapturedContext);
        }

        if (frame.CancellationToken.IsCancellationRequested)
        {
            frame.Outcome = Outcome.FromException<T>(new OperationCanceledException(frame.CancellationToken));
            return false;
        }

        return true;
    }
}
