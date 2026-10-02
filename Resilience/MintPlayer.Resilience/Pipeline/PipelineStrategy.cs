namespace MintPlayer.Resilience.Pipeline;

/// <summary>
/// One strategy of a runtime pipeline: the hook contract the interpreter (<see cref="PipelineCore{T}"/>)
/// drives. A strategy never wraps or calls "next": the interpreter walks the strategies inward calling
/// <see cref="EnterAsync"/>, invokes the user callback once at the bottom, then walks outward calling
/// <see cref="ExitAsync"/>. Everything runs inside the interpreter's single async method, so a
/// suspending callback costs one (pooled) state-machine box, whatever the number of strategies.
/// </summary>
/// <remarks>
/// <para>
/// Instances are shared by all concurrent executions and must be immutable apart from thread-safe
/// shared state (a breaker controller, a limiter). Per-execution state goes in
/// <c>frame.Slots[index]</c>, where <c>index</c> is the strategy's position in the pipeline.
/// </para>
/// <para>
/// Hooks return a <see cref="ValueTask{TResult}"/> so a strategy can await its own events and delays
/// (OnRetry, a backoff delay, a fallback action). On the happy path a hook must complete
/// synchronously (<c>new ValueTask&lt;bool&gt;(value)</c>) and must not allocate; slow paths may be
/// <c>async</c> methods with <c>[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder&lt;&gt;))]</c>.
/// The frame is a class, so an async slow path may write <c>frame.Outcome</c> and the slot after an await.
/// </para>
/// <para>
/// Pairing: <see cref="ExitAsync"/> runs exactly once for every <see cref="EnterAsync"/> that returned
/// <see langword="true"/>. When <see cref="EnterAsync"/> returns <see langword="false"/> or throws, that
/// strategy's own exit does not run, so it must release whatever it acquired before returning. An
/// exception thrown by a hook becomes the outcome (<see cref="Outcome.FromException{TResult}"/>) that
/// the outer strategies see, as in Polly.
/// </para>
/// </remarks>
internal abstract class PipelineStrategy<T>
{
    /// <summary>
    /// Called on the way in, outermost strategy first. Return <see langword="true"/> to continue inward.
    /// Return <see langword="false"/> to short-circuit: the strategy must have set <c>frame.Outcome</c>
    /// (a rejection such as <c>Outcome.Rejected&lt;T&gt;(RejectionKind.CircuitOpen, …)</c>, or an injected
    /// outcome), and the inner strategies and the callback do not run.
    /// </summary>
    /// <param name="frame">The execution.</param>
    /// <param name="index">This strategy's position, i.e. its slot in <c>frame.Slots</c>.</param>
    public virtual ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index) => new(true);

    /// <summary>
    /// Called on the way out, innermost strategy first, with <c>frame.Outcome</c> holding the inner
    /// outcome. The strategy may replace the outcome. Return <see langword="true"/> to run the inner
    /// part of the pipeline again (retry): the interpreter then calls <see cref="EnterAsync"/> of the
    /// strategies inside this one and the callback again, then this strategy's
    /// <see cref="ExitAsync"/> again. Return <see langword="false"/> to continue outward.
    /// </summary>
    /// <param name="frame">The execution.</param>
    /// <param name="index">This strategy's position, i.e. its slot in <c>frame.Slots</c>.</param>
    public virtual ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index) => new(false);
}

/// <summary>
/// A strategy that runs the inner remainder of the pipeline itself, any number of times and concurrently
/// (hedging). When the interpreter reaches it on the way in, it does not call <see cref="PipelineStrategy{T}.EnterAsync"/>
/// or <see cref="PipelineStrategy{T}.ExitAsync"/>; it calls <see cref="ExecuteAsync"/> instead, which stands in
/// for "the strategies inside this one plus the callback" and must leave the final outcome in <c>frame.Outcome</c>.
/// </summary>
/// <remarks>
/// Every run of <see cref="InnerPipeline{T}.ExecuteAsync"/> gets its own <see cref="ExecutionFrame{T}"/> (own slots,
/// own token, the given context), so the strategies inside run per attempt and concurrent attempts never share
/// state. <see cref="ExecuteAsync"/> must not complete before every run it started has completed: the
/// inner runner is pooled and reused once it returns. An exception thrown by
/// <see cref="ExecuteAsync"/> becomes the outcome, as for a hook.
/// </remarks>
internal abstract class ForkingStrategy<T> : PipelineStrategy<T>
{
    /// <summary>Runs the inner remainder of the pipeline as often as the strategy needs and sets <c>frame.Outcome</c>.</summary>
    /// <param name="frame">The execution, at this strategy's depth.</param>
    /// <param name="index">This strategy's position.</param>
    /// <param name="inner">Runs the strategies inside this one and the callback once per call.</param>
    public abstract ValueTask ExecuteAsync(ExecutionFrame<T> frame, int index, InnerPipeline<T> inner);

    /// <summary>Not called for a forking strategy.</summary>
    public sealed override ValueTask<bool> EnterAsync(ExecutionFrame<T> frame, int index) => new(true);

    /// <summary>Not called for a forking strategy.</summary>
    public sealed override ValueTask<bool> ExitAsync(ExecutionFrame<T> frame, int index) => new(false);
}

/// <summary>
/// The part of a pipeline inside a <see cref="ForkingStrategy{T}"/>: its strategies plus the user callback.
/// Created (pooled) by the interpreter for one execution of the forking strategy.
/// </summary>
internal abstract class InnerPipeline<T>
{
    protected InnerPipeline() => Callback = ExecuteAsync;

    /// <summary>
    /// Runs the inner part once on a new frame bound to <paramref name="context"/>, whose token is the one
    /// the inner strategies start from. The caller owns the context. Never throws: failures are outcomes.
    /// The returned task is pooled: await it exactly once.
    /// </summary>
    public abstract ValueTask<Outcome<T>> ExecuteAsync(ResilienceContext context);

    /// <summary><see cref="ExecuteAsync"/> as a cached delegate (the hedging <c>ActionGenerator</c> callback).</summary>
    public Func<ResilienceContext, ValueTask<Outcome<T>>> Callback { get; }
}

/// <summary>
/// A strategy that is not tied to one result type (timeout today; circuit breaker, limiters and chaos
/// later). Created once per <c>Build()</c>, so state shared by every result type (a CTS pool, a breaker
/// controller) lives here; <see cref="Create{TResult}"/> produces the typed hooks on demand, once per
/// result type a non-generic <see cref="ResiliencePipeline"/> executes.
/// </summary>
internal abstract class StrategyFactory
{
    public abstract PipelineStrategy<TResult> Create<TResult>();
}

/// <summary>What a strategy gets from the builder when it is created.</summary>
/// <remarks>M6 adds the telemetry source (pipeline name, instance name, strategy name) here.</remarks>
internal readonly struct StrategyBuildContext(TimeProvider timeProvider)
{
    public TimeProvider TimeProvider { get; } = timeProvider;
}
