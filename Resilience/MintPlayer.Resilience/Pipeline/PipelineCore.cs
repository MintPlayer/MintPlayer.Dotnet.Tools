using System.Runtime.CompilerServices;

namespace MintPlayer.Resilience.Pipeline;

/// <summary>
/// The runtime interpreter for one result type: a single async method over the strategies' hooks
/// (plan S1 decision 2). Public pipelines are thin shells that normalize their overloads to an
/// <see cref="ICallback{T}"/> and call <see cref="ExecuteAsync{TCallback, TShape, TOut}"/>.
/// </summary>
internal sealed class PipelineCore<T>
{
    private readonly PipelineStrategy<T>[] _strategies;
    private readonly bool _pooledAsync;

    public PipelineCore(PipelineStrategy<T>[] strategies, bool pooledAsync)
    {
        _strategies = strategies;
        _pooledAsync = pooledAsync;
    }

    public static PipelineCore<T> Empty { get; } = new([], pooledAsync: true);

    /// <summary>
    /// Runs one execution. The returned task is pooled (S2) unless the pipeline opted out with
    /// <c>UsePooledAsync(false)</c>, in which case a task that did not complete synchronously is
    /// converted to a <see cref="Task{TResult}"/>, which may be awaited any number of times.
    /// </summary>
    public ValueTask<TOut> ExecuteAsync<TCallback, TShape, TOut>(TCallback callback, CancellationToken cancellationToken, ResilienceContext? context, bool isSynchronous)
        where TCallback : struct, ICallback<T>
        where TShape : struct, IOutcomeShape<T, TOut>
    {
        var frame = ExecutionFrame<T>.Rent(_strategies.Length, cancellationToken, context, isSynchronous);
        var task = RunAsync<TCallback, TShape, TOut, NoTelemetry>(frame, callback, default);
        return _pooledAsync || task.IsCompleted ? task : new ValueTask<TOut>(task.AsTask());
    }

    /// <summary>Runs one synchronous execution (<c>Execute</c>): delays block, and the result is returned directly.</summary>
    public TOut Execute<TCallback, TShape, TOut>(TCallback callback, CancellationToken cancellationToken, ResilienceContext? context)
        where TCallback : struct, ICallback<T>
        where TShape : struct, IOutcomeShape<T, TOut>
    {
        var task = ExecuteAsync<TCallback, TShape, TOut>(callback, cancellationToken, context, isSynchronous: true);
        return task.IsCompleted ? task.GetAwaiter().GetResult() : task.AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Adapts a void execution to a non-generic <see cref="ValueTask"/>, allocation-free when it completes synchronously.</summary>
    public ValueTask ToVoidTask(ValueTask<VoidResult> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            _ = task.Result;
            return default;
        }

        return _pooledAsync ? AwaitVoidAsync(task) : new ValueTask(task.AsTask());
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private static async ValueTask AwaitVoidAsync(ValueTask<VoidResult> task) => await task.ConfigureAwait(false);

    /// <summary>
    /// The interpreter. Inward: <c>EnterAsync</c> of each strategy until one short-circuits; then the
    /// callback. Outward: <c>ExitAsync</c> of each entered strategy, innermost first; a strategy that
    /// asks to repeat (retry) sends the walk inward again from just inside itself.
    /// </summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<TOut> RunAsync<TCallback, TShape, TOut, TTelemetry>(ExecutionFrame<T> frame, TCallback callback, TTelemetry telemetry)
        where TCallback : struct, ICallback<T>
        where TShape : struct, IOutcomeShape<T, TOut>
        where TTelemetry : struct, IPipelineTelemetry
    {
        var strategies = _strategies;
        var continueOnCapturedContext = frame.ContinueOnCapturedContext;
        var started = telemetry.OnPipelineExecuting(frame);
        var depth = 0;

        while (true)
        {
            while (depth < strategies.Length)
            {
                bool proceed;
                try
                {
                    proceed = await strategies[depth].EnterAsync(frame, depth).ConfigureAwait(continueOnCapturedContext);
                }
                catch (Exception ex)
                {
                    frame.Outcome = new(ex);
                    proceed = false;
                }

                if (!proceed)
                {
                    break;
                }

                depth++;
            }

            if (depth == strategies.Length)
            {
                try
                {
                    if (TCallback.IsVoid)
                    {
                        await callback.InvokeVoidAsync(frame).ConfigureAwait(continueOnCapturedContext);
                        frame.Outcome = new((T)(object)VoidResult.Instance);
                    }
                    else
                    {
                        frame.Outcome = new(await callback.InvokeAsync(frame).ConfigureAwait(continueOnCapturedContext));
                    }
                }
                catch (Exception ex)
                {
                    frame.Outcome = new(ex);
                }
            }

            var repeat = false;
            while (depth > 0)
            {
                depth--;
                try
                {
                    repeat = await strategies[depth].ExitAsync(frame, depth).ConfigureAwait(continueOnCapturedContext);
                }
                catch (Exception ex)
                {
                    frame.Outcome = new(ex);
                    repeat = false;
                }

                if (repeat)
                {
                    depth++;
                    break;
                }
            }

            if (!repeat)
            {
                break;
            }
        }

        telemetry.OnPipelineExecuted(frame, started);
        var outcome = frame.Outcome;
        frame.Return();
        return TShape.Complete(outcome);
    }
}
