using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience;

/// <summary>
/// A built pipeline of strategies that executes callbacks of any result type, and void callbacks.
/// Thread-safe; build it once and share it. Create one with <see cref="ResiliencePipelineBuilder"/>.
/// </summary>
/// <remarks>
/// Strategy state (a circuit breaker's health, a timeout's CTS pool) is shared by all result types. The
/// typed hooks for a result type are created on its first execution. The returned
/// <see cref="ValueTask"/> is pooled by default: await it exactly once (see
/// <see cref="ResiliencePipelineBuilderExtensions.UsePooledAsync{TBuilder}"/>).
/// </remarks>
public sealed class ResiliencePipeline
{
    private readonly StrategyFactory[] _factories;
    private readonly bool _pooledAsync;
    private readonly PipelineCore<VoidResult> _void;
    private readonly Lock _lock = new();

    // One PipelineCore<TResult> per result type executed so far; copy-on-write, scanned linearly (a pipeline sees few result types).
    private object[] _cores = [];

    internal ResiliencePipeline(StrategyFactory[] factories, bool pooledAsync)
    {
        _factories = factories;
        _pooledAsync = pooledAsync;
        _void = CreateCore<VoidResult>();
    }

    /// <summary>Gets a pipeline without strategies: it only runs the callback.</summary>
    public static ResiliencePipeline Empty { get; } = new([], pooledAsync: true);

    /// <summary>Executes a void <paramref name="callback"/> through the pipeline.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes when the execution does; a failure or rejection is thrown.</returns>
    public ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _void.ToVoidTask(_void.ExecuteAsync<AsyncVoidCallback<Func<CancellationToken, ValueTask>>, ResultShape<VoidResult>, VoidResult>(
            new(static (f, ct) => f(ct), callback), cancellationToken, null, false));
    }

    /// <summary>Executes a void <paramref name="callback"/> with <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback; a <c>static</c> lambda keeps the call allocation-free.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes when the execution does; a failure or rejection is thrown.</returns>
    public ValueTask ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask> callback, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _void.ToVoidTask(_void.ExecuteAsync<AsyncVoidCallback<TState>, ResultShape<VoidResult>, VoidResult>(
            new(callback, state), cancellationToken, null, false));
    }

    /// <summary>Executes a void <paramref name="callback"/> with a caller-supplied context through the pipeline.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <returns>A task that completes when the execution does; a failure or rejection is thrown.</returns>
    public ValueTask ExecuteAsync(Func<ResilienceContext, ValueTask> callback, ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return _void.ToVoidTask(_void.ExecuteAsync<AsyncVoidContextCallback<Func<ResilienceContext, ValueTask>>, ResultShape<VoidResult>, VoidResult>(
            new(static (c, f) => f(c), callback), default, context, false));
    }

    /// <summary>Executes a void <paramref name="callback"/> with a caller-supplied context and <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <returns>A task that completes when the execution does; a failure or rejection is thrown.</returns>
    public ValueTask ExecuteAsync<TState>(Func<ResilienceContext, TState, ValueTask> callback, ResilienceContext context, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return _void.ToVoidTask(_void.ExecuteAsync<AsyncVoidContextCallback<TState>, ResultShape<VoidResult>, VoidResult>(
            new(callback, state), default, context, false));
    }

    /// <summary>Executes <paramref name="callback"/> through the pipeline.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Core<TResult>().ExecuteAsync<AsyncCallback<Func<CancellationToken, ValueTask<TResult>>, TResult>, ResultShape<TResult>, TResult>(
            new(static (f, ct) => f(ct), callback), cancellationToken, null, false);
    }

    /// <summary>Executes <paramref name="callback"/> with <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback; a <c>static</c> lambda keeps the call allocation-free.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public ValueTask<TResult> ExecuteAsync<TResult, TState>(Func<TState, CancellationToken, ValueTask<TResult>> callback, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Core<TResult>().ExecuteAsync<AsyncCallback<TState, TResult>, ResultShape<TResult>, TResult>(new(callback, state), cancellationToken, null, false);
    }

    /// <summary>Executes <paramref name="callback"/> with a caller-supplied context through the pipeline.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public ValueTask<TResult> ExecuteAsync<TResult>(Func<ResilienceContext, ValueTask<TResult>> callback, ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return Core<TResult>().ExecuteAsync<AsyncContextCallback<Func<ResilienceContext, ValueTask<TResult>>, TResult>, ResultShape<TResult>, TResult>(
            new(static (c, f) => f(c), callback), default, context, false);
    }

    /// <summary>Executes <paramref name="callback"/> with a caller-supplied context and <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public ValueTask<TResult> ExecuteAsync<TResult, TState>(Func<ResilienceContext, TState, ValueTask<TResult>> callback, ResilienceContext context, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return Core<TResult>().ExecuteAsync<AsyncContextCallback<TState, TResult>, ResultShape<TResult>, TResult>(new(callback, state), default, context, false);
    }

    /// <summary>Executes <paramref name="callback"/> and returns the outcome instead of throwing.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The outcome: a result, the callback's exception, or a rejection (<see cref="Outcome{TResult}.Rejection"/>), which allocates nothing.</returns>
    public ValueTask<Outcome<TResult>> TryExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Core<TResult>().ExecuteAsync<AsyncCallback<Func<CancellationToken, ValueTask<TResult>>, TResult>, OutcomeShape<TResult>, Outcome<TResult>>(
            new(static (f, ct) => f(ct), callback), cancellationToken, null, false);
    }

    /// <summary>Executes <paramref name="callback"/> with <paramref name="state"/> and returns the outcome instead of throwing.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback; a <c>static</c> lambda keeps the call allocation-free.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The outcome: a result, the callback's exception, or a rejection.</returns>
    public ValueTask<Outcome<TResult>> TryExecuteAsync<TResult, TState>(Func<TState, CancellationToken, ValueTask<TResult>> callback, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Core<TResult>().ExecuteAsync<AsyncCallback<TState, TResult>, OutcomeShape<TResult>, Outcome<TResult>>(new(callback, state), cancellationToken, null, false);
    }

    /// <summary>Executes <paramref name="callback"/> with a caller-supplied context and returns the outcome instead of throwing.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <returns>The outcome: a result, the callback's exception, or a rejection.</returns>
    public ValueTask<Outcome<TResult>> TryExecuteAsync<TResult>(Func<ResilienceContext, ValueTask<TResult>> callback, ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return Core<TResult>().ExecuteAsync<AsyncContextCallback<Func<ResilienceContext, ValueTask<TResult>>, TResult>, OutcomeShape<TResult>, Outcome<TResult>>(
            new(static (c, f) => f(c), callback), default, context, false);
    }

    /// <summary>Executes <paramref name="callback"/> with a caller-supplied context and <paramref name="state"/>, and returns the outcome instead of throwing.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <returns>The outcome: a result, the callback's exception, or a rejection.</returns>
    public ValueTask<Outcome<TResult>> TryExecuteAsync<TResult, TState>(Func<ResilienceContext, TState, ValueTask<TResult>> callback, ResilienceContext context, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return Core<TResult>().ExecuteAsync<AsyncContextCallback<TState, TResult>, OutcomeShape<TResult>, Outcome<TResult>>(new(callback, state), default, context, false);
    }

    /// <summary>Executes a synchronous void <paramref name="callback"/> through the pipeline. Delays block the calling thread.</summary>
    /// <param name="callback">The callback.</param>
    public void Execute(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _void.Execute<SyncVoidCallback<Action>, ResultShape<VoidResult>, VoidResult>(new(static (f, _) => f(), callback), default, null);
    }

    /// <summary>Executes a synchronous void <paramref name="callback"/> through the pipeline. Delays block the calling thread.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    public void Execute(Action<CancellationToken> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _void.Execute<SyncVoidCallback<Action<CancellationToken>>, ResultShape<VoidResult>, VoidResult>(new(static (f, ct) => f(ct), callback), cancellationToken, null);
    }

    /// <summary>Executes a synchronous void <paramref name="callback"/> with <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="state">The state passed to the callback.</param>
    public void Execute<TState>(Action<TState> callback, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _void.Execute<SyncVoidCallback<(Action<TState> Callback, TState State)>, ResultShape<VoidResult>, VoidResult>(
            new(static (s, _) => s.Callback(s.State), (callback, state)), default, null);
    }

    /// <summary>Executes a synchronous void <paramref name="callback"/> with <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    public void Execute<TState>(Action<TState, CancellationToken> callback, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _void.Execute<SyncVoidCallback<TState>, ResultShape<VoidResult>, VoidResult>(new(callback, state), cancellationToken, null);
    }

    /// <summary>Executes a synchronous void <paramref name="callback"/> with a caller-supplied context through the pipeline.</summary>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    public void Execute(Action<ResilienceContext> callback, ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        _void.Execute<SyncVoidContextCallback<Action<ResilienceContext>>, ResultShape<VoidResult>, VoidResult>(new(static (c, f) => f(c), callback), default, context);
    }

    /// <summary>Executes a synchronous void <paramref name="callback"/> with a caller-supplied context and <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <param name="state">The state passed to the callback.</param>
    public void Execute<TState>(Action<ResilienceContext, TState> callback, ResilienceContext context, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        _void.Execute<SyncVoidContextCallback<TState>, ResultShape<VoidResult>, VoidResult>(new(callback, state), default, context);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> through the pipeline. Delays block the calling thread.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public TResult Execute<TResult>(Func<TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Core<TResult>().Execute<SyncCallback<Func<TResult>, TResult>, ResultShape<TResult>, TResult>(new(static (f, _) => f(), callback), default, null);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> through the pipeline. Delays block the calling thread.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public TResult Execute<TResult>(Func<CancellationToken, TResult> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Core<TResult>().Execute<SyncCallback<Func<CancellationToken, TResult>, TResult>, ResultShape<TResult>, TResult>(
            new(static (f, ct) => f(ct), callback), cancellationToken, null);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> with <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public TResult Execute<TResult, TState>(Func<TState, TResult> callback, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Core<TResult>().Execute<SyncCallback<(Func<TState, TResult> Callback, TState State), TResult>, ResultShape<TResult>, TResult>(
            new(static (s, _) => s.Callback(s.State), (callback, state)), default, null);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> with <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public TResult Execute<TResult, TState>(Func<TState, CancellationToken, TResult> callback, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Core<TResult>().Execute<SyncCallback<TState, TResult>, ResultShape<TResult>, TResult>(new(callback, state), cancellationToken, null);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> with a caller-supplied context through the pipeline.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public TResult Execute<TResult>(Func<ResilienceContext, TResult> callback, ResilienceContext context)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return Core<TResult>().Execute<SyncContextCallback<Func<ResilienceContext, TResult>, TResult>, ResultShape<TResult>, TResult>(
            new(static (c, f) => f(c), callback), default, context);
    }

    /// <summary>Executes a synchronous <paramref name="callback"/> with a caller-supplied context and <paramref name="state"/> through the pipeline.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <param name="context">The context; its <see cref="ResilienceContext.CancellationToken"/> is the caller's token.</param>
    /// <param name="state">The state passed to the callback.</param>
    /// <returns>The result; a failure or rejection is thrown.</returns>
    public TResult Execute<TResult, TState>(Func<ResilienceContext, TState, TResult> callback, ResilienceContext context, TState state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);
        return Core<TResult>().Execute<SyncContextCallback<TState, TResult>, ResultShape<TResult>, TResult>(new(callback, state), default, context);
    }

    private PipelineCore<TResult> Core<TResult>()
    {
        foreach (var core in Volatile.Read(ref _cores))
        {
            if (core is PipelineCore<TResult> typed)
            {
                return typed;
            }
        }

        return AddCore<TResult>();
    }

    private PipelineCore<TResult> AddCore<TResult>()
    {
        lock (_lock)
        {
            foreach (var core in _cores)
            {
                if (core is PipelineCore<TResult> typed)
                {
                    return typed;
                }
            }

            var created = CreateCore<TResult>();
            Volatile.Write(ref _cores, [.. _cores, created]);
            return created;
        }
    }

    private PipelineCore<TResult> CreateCore<TResult>()
    {
        if (_factories.Length == 0)
        {
            return _pooledAsync ? PipelineCore<TResult>.Empty : new PipelineCore<TResult>([], pooledAsync: false);
        }

        var strategies = new PipelineStrategy<TResult>[_factories.Length];
        for (var i = 0; i < strategies.Length; i++)
        {
            strategies[i] = _factories[i].Create<TResult>();
        }

        return new PipelineCore<TResult>(strategies, _pooledAsync);
    }
}
