using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience;

/// <summary>
/// A built pipeline of strategies for callbacks returning <typeparamref name="T"/>. Thread-safe; build it
/// once and share it. Create one with <see cref="ResiliencePipelineBuilder{TResult}"/>.
/// </summary>
/// <typeparam name="T">The type of the result.</typeparam>
/// <remarks>
/// The returned <see cref="ValueTask{TResult}"/> is pooled by default: await it exactly once (see
/// <see cref="ResiliencePipelineBuilderExtensions.UsePooledAsync{TBuilder}"/>). Pass a <c>static</c> lambda
/// plus a state argument to keep a call allocation-free.
/// </remarks>
public sealed class ResiliencePipeline<T>
{
    private readonly PipelineCore<T> _core;

    internal ResiliencePipeline(PipelineCore<T> core) => _core = core;

    /// <summary>Gets a pipeline without strategies: it only runs the callback.</summary>
    public static ResiliencePipeline<T> Empty { get; } = new(PipelineCore<T>.Empty);

    /// <summary>Executes <paramref name="callback"/> through the pipeline.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public ValueTask<T> ExecuteAsync(Func<CancellationToken, ValueTask<T>> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _core.ExecuteAsync<AsyncCallback<Func<CancellationToken, ValueTask<T>>, T>, ResultShape<T>, T>(
            new(static (f, ct) => f(ct), callback), cancellationToken, null, false);
    }

    /// <summary>Executes <paramref name="callback"/> with <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback; a <c>static</c> lambda keeps the call allocation-free.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public ValueTask<T> ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<T>> callback, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _core.ExecuteAsync<AsyncCallback<TState, T>, ResultShape<T>, T>(new(callback, state), cancellationToken, null, false);
    }

    /// <summary>Executes <paramref name="callback"/> with a caller-supplied context through the pipeline.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public ValueTask<T> ExecuteAsync(Func<ResilienceContext, ValueTask<T>> callback, ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return _core.ExecuteAsync<AsyncContextCallback<Func<ResilienceContext, ValueTask<T>>, T>, ResultShape<T>, T>(
            new(static (c, f) => f(c), callback), default, context, false);
    }

    /// <summary>Executes <paramref name="callback"/> with a caller-supplied context and <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public ValueTask<T> ExecuteAsync<TState>(Func<ResilienceContext, TState, ValueTask<T>> callback, ResilienceContext context, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return _core.ExecuteAsync<AsyncContextCallback<TState, T>, ResultShape<T>, T>(new(callback, state), default, context, false);
    }

    /// <summary>Executes <paramref name="callback"/> and returns the outcome instead of throwing.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The outcome: a result, the callback's exception, or a rejection (<see cref="Outcome{TResult}.Rejection"/>), which allocates nothing.</returns>
    public ValueTask<Outcome<T>> TryExecuteAsync(Func<CancellationToken, ValueTask<T>> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _core.ExecuteAsync<AsyncCallback<Func<CancellationToken, ValueTask<T>>, T>, OutcomeShape<T>, Outcome<T>>(
            new(static (f, ct) => f(ct), callback), cancellationToken, null, false);
    }

    /// <summary>Executes <paramref name="callback"/> with <paramref name="state"/> and returns the outcome instead of throwing.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback; a <c>static</c> lambda keeps the call allocation-free.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The outcome: a result, the callback's exception, or a rejection.</returns>
    public ValueTask<Outcome<T>> TryExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask<T>> callback, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _core.ExecuteAsync<AsyncCallback<TState, T>, OutcomeShape<T>, Outcome<T>>(new(callback, state), cancellationToken, null, false);
    }

    /// <summary>Executes <paramref name="callback"/> with a caller-supplied context and returns the outcome instead of throwing.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <returns>The outcome: a result, the callback's exception, or a rejection.</returns>
    public ValueTask<Outcome<T>> TryExecuteAsync(Func<ResilienceContext, ValueTask<T>> callback, ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return _core.ExecuteAsync<AsyncContextCallback<Func<ResilienceContext, ValueTask<T>>, T>, OutcomeShape<T>, Outcome<T>>(
            new(static (c, f) => f(c), callback), default, context, false);
    }

    /// <summary>Executes <paramref name="callback"/> with a caller-supplied context and <paramref name="state"/>, and returns the outcome instead of throwing.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <returns>The outcome: a result, the callback's exception, or a rejection.</returns>
    public ValueTask<Outcome<T>> TryExecuteAsync<TState>(Func<ResilienceContext, TState, ValueTask<T>> callback, ResilienceContext context, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return _core.ExecuteAsync<AsyncContextCallback<TState, T>, OutcomeShape<T>, Outcome<T>>(new(callback, state), default, context, false);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> through the pipeline. Delays block the calling thread.</summary>
    /// <param name="callback">The callback.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public T Execute(Func<T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _core.Execute<SyncCallback<Func<T>, T>, ResultShape<T>, T>(new(static (f, _) => f(), callback), default, null);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> through the pipeline. Delays block the calling thread.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public T Execute(Func<CancellationToken, T> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _core.Execute<SyncCallback<Func<CancellationToken, T>, T>, ResultShape<T>, T>(new(static (f, ct) => f(ct), callback), cancellationToken, null);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> with <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public T Execute<TState>(Func<TState, T> callback, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _core.Execute<SyncCallback<(Func<TState, T> Callback, TState State), T>, ResultShape<T>, T>(
            new(static (s, _) => s.Callback(s.State), (callback, state)), default, null);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> with <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public T Execute<TState>(Func<TState, CancellationToken, T> callback, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _core.Execute<SyncCallback<TState, T>, ResultShape<T>, T>(new(callback, state), cancellationToken, null);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> with a caller-supplied context through the pipeline.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public T Execute(Func<ResilienceContext, T> callback, ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return _core.Execute<SyncContextCallback<Func<ResilienceContext, T>, T>, ResultShape<T>, T>(new(static (c, f) => f(c), callback), default, context);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> with a caller-supplied context and <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public T Execute<TState>(Func<ResilienceContext, TState, T> callback, ResilienceContext context, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return _core.Execute<SyncContextCallback<TState, T>, ResultShape<T>, T>(new(callback, state), default, context);
    }
}
