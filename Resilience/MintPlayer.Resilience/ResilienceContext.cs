namespace MintPlayer.Resilience;

/// <summary>
/// Per-execution state that flows through a pipeline: the cancellation token, an operation key and
/// typed <see cref="Properties"/>. Rent one from <see cref="ResilienceContextPool"/> and return it after use.
/// </summary>
/// <remarks>
/// Passing a context is optional. When a call does not pass one, the pipeline only rents a context
/// if a strategy delegate actually reads <c>args.Context</c>.
/// </remarks>
public sealed class ResilienceContext
{
    internal ResilienceContext()
    {
    }

    /// <summary>Gets the operation key, a free-form name for the operation being executed.</summary>
    public string? OperationKey { get; internal set; }

    /// <summary>
    /// Gets the cancellation token of the execution. Inside a pipeline this is the token of the current
    /// strategy level, so a timeout strategy replaces it with its own linked token.
    /// </summary>
    public CancellationToken CancellationToken { get; internal set; }

    /// <summary>Gets a value indicating whether awaits inside the pipeline continue on the captured context.</summary>
    public bool ContinueOnCapturedContext { get; internal set; }

    /// <summary>Gets the typed properties of this execution.</summary>
    public ResilienceProperties Properties { get; } = new();

    /// <summary>Whether the execution is synchronous (<c>Execute</c>), so delays block instead of awaiting.</summary>
    internal bool IsSynchronous { get; set; }

    /// <summary>Makes this context a copy of <paramref name="context"/> with its own token (a hedged attempt's context, as in Polly).</summary>
    internal void InitializeFrom(ResilienceContext context, CancellationToken cancellationToken)
    {
        OperationKey = context.OperationKey;
        IsSynchronous = context.IsSynchronous;
        ContinueOnCapturedContext = context.ContinueOnCapturedContext;
        CancellationToken = cancellationToken;
        Properties.AddOrReplaceProperties(context.Properties);
    }

    internal void Reset()
    {
        OperationKey = null;
        CancellationToken = default;
        ContinueOnCapturedContext = false;
        IsSynchronous = false;
        Properties.Clear();
    }
}
