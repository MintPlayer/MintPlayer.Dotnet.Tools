namespace MintPlayer.Resilience;

/// <summary>
/// Implemented by the generated part of every <see cref="ResiliencePipelineAttribute"/> class. Static abstract
/// members are the seam dependency injection (M6) calls through a single type argument
/// (<c>services.AddResiliencePipeline&lt;CatalogPipeline&gt;()</c>), which is also why a declarative pipeline is a
/// <c>sealed partial class</c> and not a <c>static class</c>: a static class cannot be a type argument (plan S6).
/// </summary>
/// <typeparam name="TSelf">The pipeline class.</typeparam>
/// <remarks>
/// The members are implemented explicitly, so they do not clash with the pipeline's own members. The
/// generated class also has public, non-interface equivalents: <c>PipelineName</c> and <c>UseTimeProvider</c>
/// (static in the static form, instance members in the DI form).
/// </remarks>
public interface IGeneratedResiliencePipeline<TSelf>
    where TSelf : class, IGeneratedResiliencePipeline<TSelf>
{
    /// <summary>Gets the name of the pipeline: <see cref="ResiliencePipelineAttribute.Name"/>, else the class name.</summary>
    static abstract string PipelineName { get; }

    /// <summary>
    /// Gets whether the pipeline is in the DI form: instance members and per-instance state. In the static form
    /// every instance shares one process-wide pipeline.
    /// </summary>
    static abstract bool IsInstancePipeline { get; }

    /// <summary>
    /// Creates the pipeline instance a container registers as a singleton. In the DI form the constructor's
    /// parameters are resolved from <paramref name="services"/> (an unresolved parameter without a default is
    /// an <see cref="InvalidOperationException"/>); in the static form this is a stateless shell over the static
    /// members.
    /// </summary>
    /// <param name="services">The services to resolve constructor parameters from.</param>
    /// <returns>The instance.</returns>
    static abstract TSelf Create(IServiceProvider services);

    /// <summary>
    /// Replaces the clock (and, for tests, the random source of jitter and chaos) of the pipeline: the given
    /// instance in the DI form, the process-wide pipeline in the static form. The strategy state is recreated,
    /// so call it at startup, before the first execution.
    /// </summary>
    /// <param name="instance">The instance (ignored in the static form).</param>
    /// <param name="timeProvider">The clock of every strategy.</param>
    /// <param name="randomizer">Returns a value in [0, 1) for jitter and chaos; <see langword="null"/> for the shared random source.</param>
    static abstract void UseTimeProvider(TSelf instance, TimeProvider timeProvider, Func<double>? randomizer);
}

/// <summary>
/// Implemented by the generated part of a <c>[ResiliencePipeline(Reloadable = true)]</c> class (plan S6): the
/// strategy values come from a generated options class, and every execution reads one immutable snapshot of
/// them at entry, so a reload never mixes old and new values within an execution.
/// </summary>
/// <typeparam name="TSelf">The pipeline class.</typeparam>
/// <typeparam name="TOptions">The generated <c>&lt;ClassName&gt;Options</c> class, whose defaults are the attribute values.</typeparam>
public interface IReloadableResiliencePipeline<TSelf, TOptions> : IGeneratedResiliencePipeline<TSelf>
    where TSelf : class, IReloadableResiliencePipeline<TSelf, TOptions>
    where TOptions : class, new()
{
    /// <summary>Gets the configuration section bound by default: <c>"Resilience:" + PipelineName</c>.</summary>
    static abstract string DefaultSectionPath { get; }

    /// <summary>
    /// Validates <paramref name="options"/> with the runtime builder's rules, then publishes a new snapshot.
    /// A breaker, limiter or chaos strategy whose section did not change keeps its instance, so its state (a
    /// breaker's health) survives the reload; one whose section changed is recreated.
    /// </summary>
    /// <param name="instance">The instance (ignored in the static form).</param>
    /// <param name="options">The new values.</param>
    /// <param name="error">Why the values were rejected, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the values are invalid; the previous snapshot then stays live.</returns>
    static abstract bool TryApply(TSelf instance, TOptions options, out string? error);
}
