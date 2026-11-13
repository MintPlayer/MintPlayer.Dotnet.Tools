using MintPlayer.Resilience.Simmy.Behavior;
using MintPlayer.Resilience.Simmy.Fault;
using MintPlayer.Resilience.Simmy.Latency;
using MintPlayer.Resilience.Simmy.Outcomes;

namespace MintPlayer.Resilience.Simmy;

/// <summary>Adds a fault-injection chaos strategy to a builder.</summary>
public static class ChaosFaultPipelineBuilderExtensions
{
    /// <summary>Adds a fault-injection chaos strategy.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="injectionRate">The probability of an injection, 0 to 1.</param>
    /// <param name="faultGenerator">Creates the fault; <see langword="null"/> injects nothing.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddChaosFault<TBuilder>(this TBuilder builder, double injectionRate, Func<Exception?> faultGenerator)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(faultGenerator);
        return builder.AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = injectionRate,
            FaultGenerator = _ => faultGenerator(),
        });
    }

    /// <summary>Adds a fault-injection chaos strategy.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddChaosFault<TBuilder>(this TBuilder builder, ChaosFaultStrategyOptions options)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(_ => new ChaosFaultStrategyFactory(options), options);
        return builder;
    }
}

/// <summary>Adds an outcome-injection chaos strategy to a builder.</summary>
public static class ChaosOutcomePipelineBuilderExtensions
{
    /// <summary>Adds an outcome-injection chaos strategy that injects a result.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="injectionRate">The probability of an injection, 0 to 1.</param>
    /// <param name="resultGenerator">Creates the result to inject.</param>
    /// <returns>The builder.</returns>
    public static ResiliencePipelineBuilder<TResult> AddChaosOutcome<TResult>(this ResiliencePipelineBuilder<TResult> builder, double injectionRate, Func<TResult?> resultGenerator)
    {
        ArgumentNullException.ThrowIfNull(resultGenerator);
        return builder.AddChaosOutcome(new ChaosOutcomeStrategyOptions<TResult>
        {
            InjectionRate = injectionRate,
            OutcomeGenerator = _ => Outcome.FromResult(resultGenerator()),
        });
    }

    /// <summary>Adds an outcome-injection chaos strategy.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static ResiliencePipelineBuilder<TResult> AddChaosOutcome<TResult>(this ResiliencePipelineBuilder<TResult> builder, ChaosOutcomeStrategyOptions<TResult> options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddStrategy(_ => new ChaosOutcomeStrategy<TResult>(options), options);
    }
}

/// <summary>Adds a latency-injection chaos strategy to a builder.</summary>
public static class ChaosLatencyPipelineBuilderExtensions
{
    /// <summary>Adds a latency-injection chaos strategy.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="injectionRate">The probability of an injection, 0 to 1.</param>
    /// <param name="latency">The delay to inject.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddChaosLatency<TBuilder>(this TBuilder builder, double injectionRate, TimeSpan latency)
        where TBuilder : ResiliencePipelineBuilderBase
        => builder.AddChaosLatency(new ChaosLatencyStrategyOptions { InjectionRate = injectionRate, Latency = latency });

    /// <summary>Adds a latency-injection chaos strategy.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddChaosLatency<TBuilder>(this TBuilder builder, ChaosLatencyStrategyOptions options)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(context => new ChaosLatencyStrategyFactory(options, context), options);
        return builder;
    }
}

/// <summary>Adds a behavior-injection chaos strategy to a builder.</summary>
public static class ChaosBehaviorPipelineBuilderExtensions
{
    /// <summary>Adds a behavior-injection chaos strategy.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="injectionRate">The probability of an injection, 0 to 1.</param>
    /// <param name="behavior">The behavior to run before the callback.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddChaosBehavior<TBuilder>(this TBuilder builder, double injectionRate, Func<CancellationToken, ValueTask> behavior)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(behavior);
        return builder.AddChaosBehavior(new ChaosBehaviorStrategyOptions
        {
            InjectionRate = injectionRate,
            BehaviorGenerator = args => behavior(args.Context.CancellationToken),
        });
    }

    /// <summary>Adds a behavior-injection chaos strategy.</summary>
    /// <typeparam name="TBuilder">The builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="options">The options; read when the pipeline is built.</param>
    /// <returns>The builder.</returns>
    public static TBuilder AddChaosBehavior<TBuilder>(this TBuilder builder, ChaosBehaviorStrategyOptions options)
        where TBuilder : ResiliencePipelineBuilderBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddStrategyFactory(_ => new ChaosBehaviorStrategyFactory(options), options);
        return builder;
    }
}
