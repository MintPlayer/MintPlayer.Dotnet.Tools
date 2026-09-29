namespace MintPlayer.Resilience.Pipeline;

/// <summary>
/// A user callback, as a struct so the interpreter is specialized per callback shape and never boxes
/// it. Every public overload normalizes to one of eight shapes: {async, sync} × {token, context} ×
/// {result, void}; shapes without state pass the delegate itself as the state.
/// </summary>
internal interface ICallback<T>
{
    /// <summary>True when the callback returns no result; the interpreter then calls <see cref="InvokeVoidAsync"/>.</summary>
    static abstract bool IsVoid { get; }

    ValueTask<T> InvokeAsync(ExecutionFrame<T> frame);

    ValueTask InvokeVoidAsync(ExecutionFrame<T> frame);
}

internal readonly struct AsyncCallback<TState, T>(Func<TState, CancellationToken, ValueTask<T>> callback, TState state) : ICallback<T>
{
    public static bool IsVoid => false;

    public ValueTask<T> InvokeAsync(ExecutionFrame<T> frame) => callback(state, frame.CancellationToken);

    public ValueTask InvokeVoidAsync(ExecutionFrame<T> frame) => throw new NotSupportedException();
}

internal readonly struct AsyncContextCallback<TState, T>(Func<ResilienceContext, TState, ValueTask<T>> callback, TState state) : ICallback<T>
{
    public static bool IsVoid => false;

    public ValueTask<T> InvokeAsync(ExecutionFrame<T> frame) => callback(frame.Context, state);

    public ValueTask InvokeVoidAsync(ExecutionFrame<T> frame) => throw new NotSupportedException();
}

internal readonly struct SyncCallback<TState, T>(Func<TState, CancellationToken, T> callback, TState state) : ICallback<T>
{
    public static bool IsVoid => false;

    public ValueTask<T> InvokeAsync(ExecutionFrame<T> frame) => new(callback(state, frame.CancellationToken));

    public ValueTask InvokeVoidAsync(ExecutionFrame<T> frame) => throw new NotSupportedException();
}

internal readonly struct SyncContextCallback<TState, T>(Func<ResilienceContext, TState, T> callback, TState state) : ICallback<T>
{
    public static bool IsVoid => false;

    public ValueTask<T> InvokeAsync(ExecutionFrame<T> frame) => new(callback(frame.Context, state));

    public ValueTask InvokeVoidAsync(ExecutionFrame<T> frame) => throw new NotSupportedException();
}

internal readonly struct AsyncVoidCallback<TState>(Func<TState, CancellationToken, ValueTask> callback, TState state) : ICallback<VoidResult>
{
    public static bool IsVoid => true;

    public ValueTask<VoidResult> InvokeAsync(ExecutionFrame<VoidResult> frame) => throw new NotSupportedException();

    public ValueTask InvokeVoidAsync(ExecutionFrame<VoidResult> frame) => callback(state, frame.CancellationToken);
}

internal readonly struct AsyncVoidContextCallback<TState>(Func<ResilienceContext, TState, ValueTask> callback, TState state) : ICallback<VoidResult>
{
    public static bool IsVoid => true;

    public ValueTask<VoidResult> InvokeAsync(ExecutionFrame<VoidResult> frame) => throw new NotSupportedException();

    public ValueTask InvokeVoidAsync(ExecutionFrame<VoidResult> frame) => callback(frame.Context, state);
}

internal readonly struct SyncVoidCallback<TState>(Action<TState, CancellationToken> callback, TState state) : ICallback<VoidResult>
{
    public static bool IsVoid => true;

    public ValueTask<VoidResult> InvokeAsync(ExecutionFrame<VoidResult> frame) => throw new NotSupportedException();

    public ValueTask InvokeVoidAsync(ExecutionFrame<VoidResult> frame)
    {
        callback(state, frame.CancellationToken);
        return default;
    }
}

internal readonly struct SyncVoidContextCallback<TState>(Action<ResilienceContext, TState> callback, TState state) : ICallback<VoidResult>
{
    public static bool IsVoid => true;

    public ValueTask<VoidResult> InvokeAsync(ExecutionFrame<VoidResult> frame) => throw new NotSupportedException();

    public ValueTask InvokeVoidAsync(ExecutionFrame<VoidResult> frame)
    {
        callback(frame.Context, state);
        return default;
    }
}

/// <summary>What the interpreter returns: the result (throwing on failure) or the outcome itself.</summary>
internal interface IOutcomeShape<T, TOut>
{
    static abstract TOut Complete(in Outcome<T> outcome);
}

/// <summary><c>ExecuteAsync</c> / <c>Execute</c>: the result, or a throw (fresh exception per rejection).</summary>
internal readonly struct ResultShape<T> : IOutcomeShape<T, T>
{
    public static T Complete(in Outcome<T> outcome) => outcome.GetResultOrThrow();
}

/// <summary><c>TryExecuteAsync</c>: the outcome, never throwing for a rejection or a callback exception.</summary>
internal readonly struct OutcomeShape<T> : IOutcomeShape<T, Outcome<T>>
{
    public static Outcome<T> Complete(in Outcome<T> outcome) => outcome;
}

/// <summary>
/// The telemetry seam (S8): the interpreter is generic over a struct implementing this, so with
/// <see cref="NoTelemetry"/> the calls are JIT-eliminated. M6 adds the enabled implementation and
/// reports strategy events (retry, timeout, fallback) through a telemetry object held by each strategy.
/// </summary>
internal interface IPipelineTelemetry
{
    /// <summary>Called once when an execution starts; the returned timestamp is passed back to <see cref="OnPipelineExecuted{T}"/>.</summary>
    long OnPipelineExecuting(ExecutionFrame frame);

    /// <summary>Called once when an execution completes, with the final outcome in <c>frame.Outcome</c>.</summary>
    void OnPipelineExecuted<T>(ExecutionFrame<T> frame, long startTimestamp);
}

/// <summary>Telemetry off.</summary>
internal readonly struct NoTelemetry : IPipelineTelemetry
{
    public long OnPipelineExecuting(ExecutionFrame frame) => 0;

    public void OnPipelineExecuted<T>(ExecutionFrame<T> frame, long startTimestamp)
    {
    }
}
