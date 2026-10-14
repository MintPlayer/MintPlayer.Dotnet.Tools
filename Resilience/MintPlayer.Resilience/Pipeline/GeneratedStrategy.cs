using System.ComponentModel;

namespace MintPlayer.Resilience.Pipeline;

/// <summary>
/// One runtime strategy (a circuit breaker, a limiter, a chaos strategy), driven by a generated pipeline
/// through the interpreter's own hooks. A generated pipeline inlines retry, timeout and fallback, and runs
/// the strategies whose state is intrinsic (a breaker controller, a limiter) through this handle, so their
/// semantics are those of the runtime builder by construction.
/// </summary>
/// <typeparam name="T">The result type.</typeparam>
/// <remarks>
/// Created once per generated pipeline (per settings snapshot) from a one-strategy pipeline built with the
/// runtime builder, so validation, defaults and state setup are the builder's. Public only for generated
/// pipelines; not intended for direct use.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedStrategy<T>
{
    private readonly PipelineStrategy<T> _strategy;

    internal GeneratedStrategy(PipelineStrategy<T> strategy) => _strategy = strategy;

    /// <summary>Takes the strategy of a pipeline built with exactly one strategy.</summary>
    /// <param name="pipeline">A pipeline with one strategy that does not fork (not hedging).</param>
    /// <returns>The handle.</returns>
    public static GeneratedStrategy<T> From(ResiliencePipeline<T> pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return new(Single(pipeline.Core.Strategies));
    }

    internal static PipelineStrategy<T> Single(PipelineStrategy<T>[] strategies)
    {
        if (strategies.Length != 1 || strategies[0] is ForkingStrategy<T>)
        {
            throw new ArgumentException("The pipeline must hold exactly one strategy, and not a forking one (hedging).", nameof(strategies));
        }

        return strategies[0];
    }

    /// <summary>
    /// Runs the strategy's entry hook. <see langword="false"/> means it short-circuited (a rejection or an
    /// injected outcome) and set <c>frame.Outcome</c>; its exit hook must then not run.
    /// </summary>
    /// <param name="frame">The execution.</param>
    /// <param name="index">The strategy's position in the generated pipeline (its slot).</param>
    /// <returns>Whether to continue inward.</returns>
    public ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index) => _strategy.EnterAsync(frame, index);

    /// <summary>Runs the strategy's exit hook with the inner outcome in <c>frame.Outcome</c>.</summary>
    /// <param name="frame">The execution.</param>
    /// <param name="index">The strategy's position in the generated pipeline (its slot).</param>
    /// <returns>Always <see langword="false"/> for the strategies a generated pipeline drives (none of them repeats).</returns>
    public ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index) => _strategy.ExitAsync(frame, index);

    /// <summary>
    /// Releases what the strategy attached to user objects (a breaker's state provider and manual control), so the
    /// strategy that replaces it (after UseTimeProvider or a reload) can attach them. Its state is not touched.
    /// </summary>
    public void Release() => GeneratedPipelineSupport.Release(_strategy);
}

/// <summary>
/// The result-type-agnostic form of <see cref="GeneratedStrategy{T}"/>, for a generated pipeline whose
/// executions may return any type: its state (a breaker's health) is shared by every result type, as in a
/// non-generic <see cref="ResiliencePipeline"/>.
/// </summary>
/// <remarks>Public only for generated pipelines; not intended for direct use.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedStrategy
{
    private readonly ResiliencePipeline _pipeline;
    private readonly Lock _lock = new();

    // One GeneratedStrategy<TResult> per result type used so far; copy-on-write, scanned linearly.
    private object[] _typed = [];

    private GeneratedStrategy(ResiliencePipeline pipeline) => _pipeline = pipeline;

    /// <summary>Takes the strategy of a pipeline built with exactly one strategy.</summary>
    /// <param name="pipeline">A pipeline with one strategy.</param>
    /// <returns>The handle.</returns>
    public static GeneratedStrategy From(ResiliencePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline.StrategyCount != 1)
        {
            throw new ArgumentException("The pipeline must hold exactly one strategy.", nameof(pipeline));
        }

        return new(pipeline);
    }

    /// <summary>Gets the typed hooks for <typeparamref name="TResult"/>, created on first use.</summary>
    /// <typeparam name="TResult">The result type of the execution.</typeparam>
    /// <returns>The typed handle.</returns>
    public GeneratedStrategy<TResult> For<TResult>()
    {
        foreach (var typed in Volatile.Read(ref _typed))
        {
            if (typed is GeneratedStrategy<TResult> match)
            {
                return match;
            }
        }

        return Add<TResult>();
    }

    /// <summary>Releases what the strategy attached to user objects (a breaker's state provider and manual control); see <see cref="GeneratedStrategy{T}.Release"/>.</summary>
    public void Release() => _pipeline.ReleaseAttachments();

    private GeneratedStrategy<TResult> Add<TResult>()
    {
        lock (_lock)
        {
            foreach (var typed in _typed)
            {
                if (typed is GeneratedStrategy<TResult> match)
                {
                    return match;
                }
            }

            var created = new GeneratedStrategy<TResult>(GeneratedStrategy<TResult>.Single(_pipeline.Core<TResult>().Strategies));
            Volatile.Write(ref _typed, [.. _typed, created]);
            return created;
        }
    }
}
