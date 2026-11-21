using System.Runtime.ExceptionServices;

namespace S5;

/// <summary>Why our own pipeline refused the call. Zero-cost to test; this is what users switch on.</summary>
public enum RejectionKind : byte
{
    None = 0,
    CircuitOpen,
    CircuitIsolated,
    RateLimited,
    Timeout,
}

/// <summary>Base of every exception our own strategies raise. Polly users match on the derived types.</summary>
public abstract class ResilienceRejectedException(string message, TimeSpan? retryAfter, Exception? inner)
    : Exception(message, inner)
{
    public abstract RejectionKind Kind { get; }
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class CircuitOpenException(TimeSpan? retryAfter = null, Exception? inner = null)
    : ResilienceRejectedException("The circuit is open.", retryAfter, inner)
{
    public override RejectionKind Kind => RejectionKind.CircuitOpen;
}

public sealed class RateLimitedException(TimeSpan? retryAfter = null)
    : ResilienceRejectedException("The rate limiter rejected the call.", retryAfter, null)
{
    public override RejectionKind Kind => RejectionKind.RateLimited;
}

public sealed class ResilienceTimeoutException(TimeSpan? timeout = null)
    : ResilienceRejectedException("The attempt timed out.", null, null)
{
    public TimeSpan? Timeout { get; } = timeout;
    public override RejectionKind Kind => RejectionKind.Timeout;
}

/// <summary>
/// Cached sentinels. They are handed out by <see cref="Outcome{T}.Exception"/> for type-matching only and are
/// NEVER thrown by the library, so their stack trace stays null forever.
/// </summary>
public static class Rejections
{
    public static readonly CircuitOpenException CircuitOpen = new();
    public static readonly RateLimitedException RateLimited = new();
    public static readonly ResilienceTimeoutException Timeout = new();

    public static ResilienceRejectedException Sentinel(RejectionKind kind) => kind switch
    {
        RejectionKind.CircuitOpen or RejectionKind.CircuitIsolated => CircuitOpen,
        RejectionKind.RateLimited => RateLimited,
        _ => Timeout,
    };

    /// <summary>What the throwing API raises: a fresh instance per throw.</summary>
    public static ResilienceRejectedException Create(RejectionKind kind, TimeSpan? retryAfter, Exception? inner) => kind switch
    {
        RejectionKind.CircuitOpen or RejectionKind.CircuitIsolated => new CircuitOpenException(retryAfter, inner),
        RejectionKind.RateLimited => new RateLimitedException(retryAfter),
        _ => new ResilienceTimeoutException(),
    };
}

/// <summary>
/// Proposed public shape: 24 B + sizeof(T). Rejection is an enum + optional RetryAfter; the Exception
/// reference is only used for a user-callback failure (never a per-call allocation for our rejections).
/// </summary>
public readonly struct Outcome<T>
{
    private readonly T _result;
    private readonly Exception? _exception;
    private readonly long _retryAfterTicks; // -1 = none
    public RejectionKind Rejection { get; }

    public Outcome(T result) { _result = result; _exception = null; _retryAfterTicks = -1; Rejection = RejectionKind.None; }
    public Outcome(Exception exception) { _result = default!; _exception = exception; _retryAfterTicks = -1; Rejection = RejectionKind.None; }

    private Outcome(RejectionKind kind, TimeSpan? retryAfter)
    {
        _result = default!;
        _exception = null;
        _retryAfterTicks = retryAfter?.Ticks ?? -1;
        Rejection = kind;
    }

    public static Outcome<T> Rejected(RejectionKind kind, TimeSpan? retryAfter = null) => new(kind, retryAfter);

    public bool IsSuccess => _exception is null && Rejection == RejectionKind.None;
    public bool IsRejected => Rejection != RejectionKind.None;
    public T Result => _result;
    public TimeSpan? RetryAfter => _retryAfterTicks < 0 ? null : TimeSpan.FromTicks(_retryAfterTicks);

    /// <summary>User exception, or a cached never-thrown sentinel for our rejections (for `is CircuitOpenException`).</summary>
    public Exception? Exception => Rejection != RejectionKind.None ? Rejections.Sentinel(Rejection) : _exception;

    /// <summary>The only way the library throws a rejection: fresh instance, own stack, own Data.</summary>
    public T GetResultOrThrow(Exception? inner = null)
    {
        if (Rejection != RejectionKind.None) throw Rejections.Create(Rejection, RetryAfter, inner);
        if (_exception is not null) ExceptionDispatchInfo.Throw(_exception);
        return _result;
    }
}
