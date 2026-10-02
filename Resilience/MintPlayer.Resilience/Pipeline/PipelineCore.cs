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

    // _nextFork[i]: the position of the first ForkingStrategy at or after i, or _strategies.Length when there is none.
    private readonly int[] _nextFork;

    public PipelineCore(PipelineStrategy<T>[] strategies, bool pooledAsync)
    {
        _strategies = strategies;
        _pooledAsync = pooledAsync;
        _nextFork = new int[strategies.Length + 1];
        _nextFork[strategies.Length] = strategies.Length;
        for (var i = strategies.Length - 1; i >= 0; i--)
        {
            _nextFork[i] = strategies[i] is ForkingStrategy<T> ? i : _nextFork[i + 1];
        }
    }

    public static PipelineCore<T> Empty { get; } = new([], pooledAsync: true);

    /// <summary>The strategies, outermost first.</summary>
    public PipelineStrategy<T>[] Strategies => _strategies;

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
        var task = RunAsync<TCallback, TShape, TOut, NoTelemetry>(frame, callback, default, 0);
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
    /// <remarks>
    /// A <see cref="ForkingStrategy{T}"/> (hedging) ends the walk inward: in place of the callback the
    /// interpreter hands it an <see cref="InnerPipeline{T}"/> that runs this same method from just inside the
    /// forking strategy (<paramref name="start"/>) on a fresh frame per attempt. Without a forking strategy
    /// the only cost is one comparison per round.
    /// </remarks>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<TOut> RunAsync<TCallback, TShape, TOut, TTelemetry>(ExecutionFrame<T> frame, TCallback callback, TTelemetry telemetry, int start)
        where TCallback : struct, ICallback<T>
        where TShape : struct, IOutcomeShape<T, TOut>
        where TTelemetry : struct, IPipelineTelemetry
    {
        var strategies = _strategies;
        var end = _nextFork[start];
        var continueOnCapturedContext = frame.ContinueOnCapturedContext;
        var started = telemetry.OnPipelineExecuting(frame);
        var depth = start;

        while (true)
        {
            while (depth < end)
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

            if (depth == end)
            {
                try
                {
                    if (end != strategies.Length)
                    {
                        var inner = InnerRunner<TCallback>.Rent(this, callback, end + 1);
                        try
                        {
                            await ((ForkingStrategy<T>)strategies[end]).ExecuteAsync(frame, end, inner).ConfigureAwait(continueOnCapturedContext);
                        }
                        finally
                        {
                            inner.Return();
                        }
                    }
                    else if (TCallback.IsVoid)
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
            while (depth > start)
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

    /// <summary>The inner part of the pipeline below a forking strategy, bound to one execution's callback. Pooled per callback shape.</summary>
    private sealed class InnerRunner<TCallback> : InnerPipeline<T>
        where TCallback : struct, ICallback<T>
    {
        private static readonly ObjectPool<InnerRunner<TCallback>> Pool = new(static () => new InnerRunner<TCallback>());

        private PipelineCore<T>? _core;
        private TCallback _callback;
        private int _start;

        public static InnerRunner<TCallback> Rent(PipelineCore<T> core, TCallback callback, int start)
        {
            var runner = Pool.Get();
            runner._core = core;
            runner._callback = callback;
            runner._start = start;
            return runner;
        }

        public void Return()
        {
            _core = null;
            _callback = default;
            Pool.Return(this);
        }

        public override ValueTask<Outcome<T>> ExecuteAsync(ResilienceContext context)
        {
            var core = _core ?? throw new InvalidOperationException("The hedged callback was invoked after the hedging execution completed.");
            var frame = ExecutionFrame<T>.Rent(core._strategies.Length, default, context, context.IsSynchronous);
            return core.RunAsync<TCallback, OutcomeShape<T>, Outcome<T>, NoTelemetry>(frame, _callback, default, _start);
        }
    }
}
