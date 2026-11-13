using System.ComponentModel;

namespace MintPlayer.Resilience.Pipeline;

// The callback, outcome-shape and telemetry types below are public only for the code the source generator
// emits for [ResiliencePipeline] classes (M4), which runs the same callback structs as the interpreter.
// They are not intended for direct use.

/// <summary>
/// A user callback, as a struct so the interpreter is specialized per callback shape and never boxes
/// it. Every public overload normalizes to one of eight shapes: {async, sync} × {token, context} ×
/// {result, void}; shapes without state pass the delegate itself as the state.
/// </summary>
/// <typeparam name="T">The result type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ICallback<T>
{
    /// <summary>Gets whether the callback returns no result; the interpreter then calls <see cref="InvokeVoidAsync"/>.</summary>
    static abstract bool IsVoid { get; }

    /// <summary>Invokes a callback that returns a result.</summary>
    /// <param name="frame">The execution, which supplies the current token or context.</param>
    /// <returns>The callback's result.</returns>
    ValueTask<T> InvokeAsync(ExecutionFrame<T> frame);

    /// <summary>Invokes a void callback.</summary>
    /// <param name="frame">The execution, which supplies the current token or context.</param>
    /// <returns>A task that completes with the callback.</returns>
    ValueTask InvokeVoidAsync(ExecutionFrame<T> frame);
}

/// <summary>An asynchronous callback taking state and the current token.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <typeparam name="T">The result type.</typeparam>
/// <param name="callback">The user callback.</param>
/// <param name="state">The state passed to it.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct AsyncCallback<TState, T>(Func<TState, CancellationToken, ValueTask<T>> callback, TState state) : ICallback<T>
{
    /// <inheritdoc/>
    public static bool IsVoid => false;

    /// <inheritdoc/>
    public ValueTask<T> InvokeAsync(ExecutionFrame<T> frame) => callback(state, frame.CancellationToken);

    /// <inheritdoc/>
    public ValueTask InvokeVoidAsync(ExecutionFrame<T> frame) => throw new NotSupportedException();
}

/// <summary>An asynchronous callback taking the context and state.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <typeparam name="T">The result type.</typeparam>
/// <param name="callback">The user callback.</param>
/// <param name="state">The state passed to it.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct AsyncContextCallback<TState, T>(Func<ResilienceContext, TState, ValueTask<T>> callback, TState state) : ICallback<T>
{
    /// <inheritdoc/>
    public static bool IsVoid => false;

    /// <inheritdoc/>
    public ValueTask<T> InvokeAsync(ExecutionFrame<T> frame) => callback(frame.Context, state);

    /// <inheritdoc/>
    public ValueTask InvokeVoidAsync(ExecutionFrame<T> frame) => throw new NotSupportedException();
}

/// <summary>A synchronous callback taking state and the current token.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <typeparam name="T">The result type.</typeparam>
/// <param name="callback">The user callback.</param>
/// <param name="state">The state passed to it.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct SyncCallback<TState, T>(Func<TState, CancellationToken, T> callback, TState state) : ICallback<T>
{
    /// <inheritdoc/>
    public static bool IsVoid => false;

    /// <inheritdoc/>
    public ValueTask<T> InvokeAsync(ExecutionFrame<T> frame) => new(callback(state, frame.CancellationToken));

    /// <inheritdoc/>
    public ValueTask InvokeVoidAsync(ExecutionFrame<T> frame) => throw new NotSupportedException();
}

/// <summary>A synchronous callback taking the context and state.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <typeparam name="T">The result type.</typeparam>
/// <param name="callback">The user callback.</param>
/// <param name="state">The state passed to it.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct SyncContextCallback<TState, T>(Func<ResilienceContext, TState, T> callback, TState state) : ICallback<T>
{
    /// <inheritdoc/>
    public static bool IsVoid => false;

    /// <inheritdoc/>
    public ValueTask<T> InvokeAsync(ExecutionFrame<T> frame) => new(callback(frame.Context, state));

    /// <inheritdoc/>
    public ValueTask InvokeVoidAsync(ExecutionFrame<T> frame) => throw new NotSupportedException();
}

/// <summary>An asynchronous void callback taking state and the current token.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <param name="callback">The user callback.</param>
/// <param name="state">The state passed to it.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct AsyncVoidCallback<TState>(Func<TState, CancellationToken, ValueTask> callback, TState state) : ICallback<VoidResult>
{
    /// <inheritdoc/>
    public static bool IsVoid => true;

    /// <inheritdoc/>
    public ValueTask<VoidResult> InvokeAsync(ExecutionFrame<VoidResult> frame) => throw new NotSupportedException();

    /// <inheritdoc/>
    public ValueTask InvokeVoidAsync(ExecutionFrame<VoidResult> frame) => callback(state, frame.CancellationToken);
}

/// <summary>An asynchronous void callback taking the context and state.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <param name="callback">The user callback.</param>
/// <param name="state">The state passed to it.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct AsyncVoidContextCallback<TState>(Func<ResilienceContext, TState, ValueTask> callback, TState state) : ICallback<VoidResult>
{
    /// <inheritdoc/>
    public static bool IsVoid => true;

    /// <inheritdoc/>
    public ValueTask<VoidResult> InvokeAsync(ExecutionFrame<VoidResult> frame) => throw new NotSupportedException();

    /// <inheritdoc/>
    public ValueTask InvokeVoidAsync(ExecutionFrame<VoidResult> frame) => callback(frame.Context, state);
}

/// <summary>A synchronous void callback taking state and the current token.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <param name="callback">The user callback.</param>
/// <param name="state">The state passed to it.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct SyncVoidCallback<TState>(Action<TState, CancellationToken> callback, TState state) : ICallback<VoidResult>
{
    /// <inheritdoc/>
    public static bool IsVoid => true;

    /// <inheritdoc/>
    public ValueTask<VoidResult> InvokeAsync(ExecutionFrame<VoidResult> frame) => throw new NotSupportedException();

    /// <inheritdoc/>
    public ValueTask InvokeVoidAsync(ExecutionFrame<VoidResult> frame)
    {
        callback(state, frame.CancellationToken);
        return default;
    }
}

/// <summary>A synchronous void callback taking the context and state.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <param name="callback">The user callback.</param>
/// <param name="state">The state passed to it.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct SyncVoidContextCallback<TState>(Action<ResilienceContext, TState> callback, TState state) : ICallback<VoidResult>
{
    /// <inheritdoc/>
    public static bool IsVoid => true;

    /// <inheritdoc/>
    public ValueTask<VoidResult> InvokeAsync(ExecutionFrame<VoidResult> frame) => throw new NotSupportedException();

    /// <inheritdoc/>
    public ValueTask InvokeVoidAsync(ExecutionFrame<VoidResult> frame)
    {
        callback(frame.Context, state);
        return default;
    }
}

/// <summary>What the interpreter returns: the result (throwing on failure) or the outcome itself.</summary>
/// <typeparam name="T">The result type.</typeparam>
/// <typeparam name="TOut">What the execution returns.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IOutcomeShape<T, TOut>
{
    /// <summary>Turns the final outcome into what the execution returns.</summary>
    /// <param name="outcome">The final outcome.</param>
    /// <returns>The value returned to the caller.</returns>
    static abstract TOut Complete(in Outcome<T> outcome);
}

/// <summary><c>ExecuteAsync</c> / <c>Execute</c>: the result, or a throw (fresh exception per rejection).</summary>
/// <typeparam name="T">The result type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct ResultShape<T> : IOutcomeShape<T, T>
{
    /// <inheritdoc/>
    public static T Complete(in Outcome<T> outcome) => outcome.GetResultOrThrow();
}

/// <summary><c>TryExecuteAsync</c>: the outcome, never throwing for a rejection or a callback exception.</summary>
/// <typeparam name="T">The result type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct OutcomeShape<T> : IOutcomeShape<T, Outcome<T>>
{
    /// <inheritdoc/>
    public static Outcome<T> Complete(in Outcome<T> outcome) => outcome;
}

/// <summary>
/// The telemetry seam (S8): the interpreter and the generated pipelines are generic over a struct
/// implementing this, so with <see cref="NoTelemetry"/> the calls are JIT-eliminated. M6 adds the enabled
/// implementation and reports strategy events (retry, timeout, fallback) through a telemetry object held
/// by each strategy.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IPipelineTelemetry
{
    /// <summary>Called once when an execution starts; the returned timestamp is passed back to <see cref="OnPipelineExecuted{T}"/>.</summary>
    /// <param name="frame">The execution.</param>
    /// <returns>The start timestamp.</returns>
    long OnPipelineExecuting(ExecutionFrame frame);

    /// <summary>Called once when an execution completes, with the final outcome in <c>frame.Outcome</c>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="frame">The execution.</param>
    /// <param name="startTimestamp">The value <see cref="OnPipelineExecuting"/> returned.</param>
    void OnPipelineExecuted<T>(ExecutionFrame<T> frame, long startTimestamp);
}

/// <summary>Telemetry off.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct NoTelemetry : IPipelineTelemetry
{
    /// <inheritdoc/>
    public long OnPipelineExecuting(ExecutionFrame frame) => 0;

    /// <inheritdoc/>
    public void OnPipelineExecuted<T>(ExecutionFrame<T> frame, long startTimestamp)
    {
    }
}
