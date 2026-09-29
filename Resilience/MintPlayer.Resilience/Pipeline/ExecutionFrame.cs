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
internal abstract class ExecutionFrame
{
    private ResilienceContext? _context;
    private bool _ownsContext;

    /// <summary>One slot per strategy, indexed by the strategy's position in the pipeline.</summary>
    public StrategySlot[] Slots = [];

    /// <summary>The cancellation token at the current depth of the pipeline.</summary>
    public CancellationToken CancellationToken { get; private set; }

    public bool ContinueOnCapturedContext { get; private set; }

    /// <summary>True for <c>Execute</c>: delays block rather than await.</summary>
    public bool IsSynchronous { get; private set; }

    /// <summary>The caller's context, or one rented on first use (only when a delegate reads <c>args.Context</c>).</summary>
    public ResilienceContext Context => _context ?? RentContext();

    /// <summary>Replaces the current token (a timeout on the way in, the previous token on the way out).</summary>
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
internal sealed class ExecutionFrame<T> : ExecutionFrame
{
    private static readonly ObjectPool<ExecutionFrame<T>> Pool = new(static () => new ExecutionFrame<T>());

    /// <summary>
    /// The outcome at the current depth: set by the callback, or by a strategy that short-circuits in
    /// <c>EnterAsync</c>; strategies may replace it in <c>ExitAsync</c> (timeout, fallback, retry).
    /// </summary>
    public Outcome<T> Outcome;

    public static ExecutionFrame<T> Rent(int slotCount, CancellationToken cancellationToken, ResilienceContext? context, bool isSynchronous)
    {
        var frame = Pool.Get();
        frame.Initialize(slotCount, cancellationToken, context, isSynchronous);
        return frame;
    }

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
