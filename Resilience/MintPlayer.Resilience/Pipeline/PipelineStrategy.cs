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
