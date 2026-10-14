using System.ComponentModel;

namespace MintPlayer.Resilience.Pipeline;

/// <summary>
/// Per-execution scratch space for one strategy: whatever it must remember between its
/// <see cref="PipelineStrategy{T}.EnterAsync"/> and <see cref="PipelineStrategy{T}.ExitAsync"/> calls
/// (a rented CTS and the token it replaced, an attempt counter, a lease, a timestamp). Cleared when
/// the frame is returned.
/// </summary>
internal struct StrategySlot
{
    public object? Object;
    public CancellationToken Token;
    public CancellationTokenRegistration Registration;
    public long Long;
    public double Double;
    public int Int;
}

/// <summary>
/// The state of one execution, independent of the result type: the current cancellation token,
/// the (lazily rented) <see cref="ResilienceContext"/>, and one <see cref="StrategySlot"/> per strategy.
/// Pooled; a strategy must not keep a reference to it after the execution completes.
/// </summary>
/// <remarks>
/// Public only for the code the source generator emits for <c>[ResiliencePipeline]</c> classes (M4): a
/// generated pipeline runs on the same frame as the interpreter, so argument structs resolve their
/// <c>Context</c> lazily in exactly the same way. Not intended for direct use.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ExecutionFrame
{
    private ResilienceContext? _context;
    private bool _ownsContext;

    private protected ExecutionFrame()
    {
    }

    /// <summary>One slot per strategy, indexed by the strategy's position in the pipeline.</summary>
    internal StrategySlot[] Slots = [];

    /// <summary>Gets the cancellation token at the current depth of the pipeline.</summary>
    public CancellationToken CancellationToken { get; private set; }

    /// <summary>Gets whether awaits inside the pipeline continue on the captured context (from the caller's <see cref="ResilienceContext"/>).</summary>
    public bool ContinueOnCapturedContext { get; private set; }

    /// <summary>Gets whether this is a synchronous execution (<c>Execute</c>): delays block rather than await.</summary>
    public bool IsSynchronous { get; private set; }

    /// <summary>Gets the caller's context, or one rented on first use (only when a delegate reads <c>args.Context</c>).</summary>
    public ResilienceContext Context => _context ?? RentContext();

    /// <summary>Replaces the current token (a timeout on the way in, the previous token on the way out).</summary>
    /// <param name="token">The new current token.</param>
    public void SetCancellationToken(CancellationToken token)
    {
        CancellationToken = token;
        if (_context is not null)
        {
            _context.CancellationToken = token;
        }
    }

    private protected void Initialize(int slotCount, CancellationToken cancellationToken, ResilienceContext? context, bool isSynchronous)
    {
        if (Slots.Length < slotCount)
        {
            Slots = new StrategySlot[slotCount];
        }

        IsSynchronous = isSynchronous;
        if (context is null)
        {
            CancellationToken = cancellationToken;
            ContinueOnCapturedContext = false;
        }
        else
        {
            _context = context;
            _ownsContext = false;
            context.IsSynchronous = isSynchronous;
            CancellationToken = context.CancellationToken;
            ContinueOnCapturedContext = context.ContinueOnCapturedContext;
        }
    }

    private protected void ResetFrame()
    {
        Array.Clear(Slots);
        if (_ownsContext)
        {
            ResilienceContextPool.Shared.Return(_context!);
        }

        _context = null;
        _ownsContext = false;
        CancellationToken = default;
    }

    private ResilienceContext RentContext()
    {
        var context = ResilienceContextPool.Shared.Get(ContinueOnCapturedContext, CancellationToken);
        context.IsSynchronous = IsSynchronous;
        _context = context;
        _ownsContext = true;
        return context;
    }
}

/// <summary>An <see cref="ExecutionFrame"/> that also carries the current outcome.</summary>
/// <typeparam name="T">The result type of the execution.</typeparam>
/// <remarks>Public only for generated pipelines; see <see cref="ExecutionFrame"/>.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ExecutionFrame<T> : ExecutionFrame
{
    private static readonly ObjectPool<ExecutionFrame<T>> Pool = new(static () => new ExecutionFrame<T>());

    private ExecutionFrame()
    {
    }

    /// <summary>
    /// The outcome at the current depth: set by the callback, or by a strategy that short-circuits in
    /// <c>EnterAsync</c>; strategies may replace it in <c>ExitAsync</c> (timeout, fallback, retry).
    /// </summary>
    public Outcome<T> Outcome;

    /// <summary>Rents a frame from the pool.</summary>
    /// <param name="slotCount">The number of strategies of the pipeline (one slot each).</param>
    /// <param name="cancellationToken">The caller's token; ignored when <paramref name="context"/> is given.</param>
    /// <param name="context">The caller's context, or <see langword="null"/> to rent one only when a delegate reads it.</param>
    /// <param name="isSynchronous">Whether this is a synchronous execution.</param>
    /// <returns>The frame; return it with <see cref="Return"/> when the execution completes.</returns>
    public static ExecutionFrame<T> Rent(int slotCount, CancellationToken cancellationToken, ResilienceContext? context, bool isSynchronous)
    {
        var frame = Pool.Get();
        frame.Initialize(slotCount, cancellationToken, context, isSynchronous);
        return frame;
    }

    /// <summary>Clears the frame (returning a rented context) and puts it back in the pool.</summary>
    public void Return()
    {
        Outcome = default;
        ResetFrame();
        Pool.Return(this);
    }
}

/// <summary>Resolves the <c>Context</c> of an arguments struct, which holds either a context or a frame.</summary>
internal static class ContextSource
{
    public static ResilienceContext Resolve(object? source) => source as ResilienceContext ?? (source as ExecutionFrame)?.Context!;
}
