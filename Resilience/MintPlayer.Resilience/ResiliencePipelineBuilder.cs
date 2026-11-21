using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience;

/// <summary>What <see cref="ResiliencePipelineBuilder"/> and <see cref="ResiliencePipelineBuilder{TResult}"/> share.</summary>
public abstract class ResiliencePipelineBuilderBase
{
    private bool _used;

    private protected ResiliencePipelineBuilderBase()
    {
    }

    /// <summary>Gets or sets the name of the pipeline, used by telemetry.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the instance name of the pipeline, used by telemetry.</summary>
    public string? InstanceName { get; set; }

    /// <summary>Gets or sets the clock of every strategy (timeouts, delays, durations). Default <see cref="System.TimeProvider.System"/>.</summary>
    public TimeProvider? TimeProvider { get; set; }

    /// <summary>Whether executions return a pooled <see cref="ValueTask"/> (plan S2; default on). See <see cref="ResiliencePipelineBuilderExtensions.UsePooledAsync{TBuilder}"/>.</summary>
    internal bool PooledAsync { get; set; } = true;

    /// <summary>Adds a strategy that is not tied to a result type (timeout; later circuit breaker, limiters, chaos).</summary>
    internal abstract void AddStrategyFactory(Func<StrategyBuildContext, StrategyFactory> factory, ResilienceStrategyOptions options);

    private protected void EnsureCanAdd(ResilienceStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (_used)
        {
            throw new InvalidOperationException("Cannot add any more resilience strategies to the builder after it has been used to build a pipeline once.");
        }
    }

    private protected StrategyBuildContext StartBuild()
    {
        _used = true;
        return new StrategyBuildContext(TimeProvider ?? System.TimeProvider.System);
    }
}

/// <summary>Builds a <see cref="ResiliencePipeline"/>, which executes callbacks of any result type.</summary>
public sealed class ResiliencePipelineBuilder : ResiliencePipelineBuilderBase
{
    private readonly List<Func<StrategyBuildContext, StrategyFactory>> _entries = [];

    /// <summary>Builds the pipeline. Strategies run in the order they were added, the first one outermost.</summary>
    /// <returns>The pipeline.</returns>
    public ResiliencePipeline Build()
    {
        var context = StartBuild();
        return new ResiliencePipeline(_entries.ConvertAll(entry => entry(context)).ToArray(), PooledAsync);
    }

    internal override void AddStrategyFactory(Func<StrategyBuildContext, StrategyFactory> factory, ResilienceStrategyOptions options)
    {
        EnsureCanAdd(options);
        _entries.Add(factory);
    }
}

/// <summary>Builds a <see cref="ResiliencePipeline{TResult}"/>, which executes callbacks returning <typeparamref name="TResult"/>.</summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
public sealed class ResiliencePipelineBuilder<TResult> : ResiliencePipelineBuilderBase
{
    private readonly List<Func<StrategyBuildContext, PipelineStrategy<TResult>>> _entries = [];

    /// <summary>Builds the pipeline. Strategies run in the order they were added, the first one outermost.</summary>
    /// <returns>The pipeline.</returns>
    public ResiliencePipeline<TResult> Build()
    {
        var context = StartBuild();
        return new ResiliencePipeline<TResult>(new PipelineCore<TResult>(_entries.ConvertAll(entry => entry(context)).ToArray(), PooledAsync));
    }

    internal ResiliencePipelineBuilder<TResult> AddStrategy(Func<StrategyBuildContext, PipelineStrategy<TResult>> factory, ResilienceStrategyOptions options)
    {
        EnsureCanAdd(options);
        _entries.Add(factory);
        return this;
    }

    internal override void AddStrategyFactory(Func<StrategyBuildContext, StrategyFactory> factory, ResilienceStrategyOptions options)
        => AddStrategy(context => factory(context).Create<TResult>(), options);
}

/// <summary>Builder settings shared by both builders.</summary>
public static class ResiliencePipelineBuilderExtensions
{
    /// <summary>
    /// Chooses whether executions return a pooled <see cref="ValueTask"/> (the default). Pooling makes an
    /// execution whose callback really suspends allocate nothing, but the returned task must then be
    /// awaited exactly once: awaiting it twice, reading <c>.Result</c> before completion, or awaiting it
    /// from two places throws or crashes. Pass <see langword="false"/> for code that breaks those rules;
    /// a suspending execution then costs one <see cref="Task"/>.
    /// </summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="enabled">Whether to pool.</param>
    /// <returns>The builder.</returns>
    public static TBuilder UsePooledAsync<TBuilder>(this TBuilder builder, bool enabled = true)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.PooledAsync = enabled;
        return builder;
    }
}
