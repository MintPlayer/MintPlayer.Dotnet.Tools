using System.Runtime.ExceptionServices;
using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.RateLimiting;
using MintPlayer.Resilience.Timeout;

namespace MintPlayer.Resilience;

/// <summary>Creates <see cref="Outcome{TResult}"/> values.</summary>
public static class Outcome
{
    /// <summary>Creates an outcome that holds a result.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="value">The result.</param>
    /// <returns>A successful outcome.</returns>
    public static Outcome<TResult> FromResult<TResult>(TResult? value) => new(value);

    /// <summary>Creates a completed <see cref="ValueTask{TResult}"/> that holds an outcome with a result.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="value">The result.</param>
    /// <returns>A completed task holding a successful outcome.</returns>
    public static ValueTask<Outcome<TResult>> FromResultAsValueTask<TResult>(TResult value) => new(FromResult(value));

    /// <summary>Creates an outcome that holds an exception.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="exception">The exception.</param>
    /// <returns>A failed outcome.</returns>
    public static Outcome<TResult> FromException<TResult>(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(exception);
    }

    /// <summary>Creates a completed <see cref="ValueTask{TResult}"/> that holds an outcome with an exception.</summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="exception">The exception.</param>
    /// <returns>A completed task holding a failed outcome.</returns>
    public static ValueTask<Outcome<TResult>> FromExceptionAsValueTask<TResult>(Exception exception) => new(FromException<TResult>(exception));

    /// <summary>Creates a rejected outcome. <paramref name="ticks"/> is the retry-after hint, or the timeout for <see cref="RejectionKind.Timeout"/>; negative means none.</summary>
    internal static Outcome<TResult> Rejected<TResult>(RejectionKind kind, long ticks = -1, Exception? cause = null) => new(kind, ticks, cause);
}

/// <summary>
/// The result of an execution: a result, an exception, or a rejection by one of the pipeline's
/// strategies.
/// </summary>
/// <typeparam name="TResult">The type of the result.</typeparam>
/// <remarks>
/// A rejection is stored as a <see cref="RejectionKind"/> plus an optional retry-after hint, not as an
/// exception, so a rejected <c>TryExecuteAsync</c> allocates nothing. Reading <see cref="Exception"/>
/// on a rejected outcome creates a fresh exception of the matching type (for example
/// <see cref="BrokenCircuitException"/>) on every read.
/// </remarks>
public readonly struct Outcome<TResult>
{
    private readonly TResult? _result;

    // The callback's exception; for a rejection, the exception that caused it (the cancellation a timeout
    // provoked, or the exception that broke a circuit), used as the InnerException of the rejection.
    private readonly Exception? _exception;

    // The retry-after hint (circuit / limiter) or the timeout (timeout rejection), in ticks. Negative = none.
    private readonly long _ticks;
    private readonly RejectionKind _rejection;

    internal Outcome(TResult? result)
    {
        _result = result;
        _exception = null;
        _ticks = -1;
        _rejection = RejectionKind.None;
    }

    internal Outcome(Exception exception)
    {
        _result = default;
        _exception = exception;
        _ticks = -1;
        _rejection = RejectionKind.None;
    }

    internal Outcome(RejectionKind rejection, long ticks, Exception? cause)
    {
        _result = default;
        _exception = cause;
        _ticks = ticks;
        _rejection = rejection;
    }

    private Outcome(TResult? result, Exception? exception, long ticks, RejectionKind rejection)
    {
        _result = result;
        _exception = exception;
        _ticks = ticks;
        _rejection = rejection;
    }

    /// <summary>Gets a value indicating whether the execution produced a result (no exception, no rejection).</summary>
    public bool IsSuccess => _rejection == RejectionKind.None && _exception is null;

    /// <summary>Gets a value indicating whether a strategy rejected the execution.</summary>
    public bool IsRejected => _rejection != RejectionKind.None;

    /// <summary>Gets why a strategy rejected the execution, or <see cref="RejectionKind.None"/>.</summary>
    public RejectionKind Rejection => _rejection;

    /// <summary>Gets how long to wait before trying again, when a circuit breaker or limiter rejected the call and knows it.</summary>
    public TimeSpan? RetryAfter => _ticks >= 0 && _rejection is RejectionKind.CircuitOpen or RejectionKind.CircuitIsolated or RejectionKind.RateLimited
        ? TimeSpan.FromTicks(_ticks)
        : null;

    /// <summary>Gets the result. It is the default value when the outcome is not successful.</summary>
    public TResult? Result => _result;

    /// <summary>
    /// Gets the exception: the one the callback threw, or, for a rejection, a <b>fresh</b> instance of
    /// the matching <see cref="ResilienceRejectedException"/> type on every read. Test
    /// <see cref="Rejection"/> instead to avoid the allocation.
    /// </summary>
    public Exception? Exception => _rejection == RejectionKind.None ? _exception : CreateRejectionException();

    /// <summary>The callback's exception, or null for a success or a rejection. Never allocates.</summary>
    internal Exception? RawException => _rejection == RejectionKind.None ? _exception : null;

    /// <summary>For a rejection, the exception that caused it.</summary>
    internal Exception? Cause => _rejection == RejectionKind.None ? null : _exception;

    /// <summary>The raw retry-after / timeout ticks (negative = none).</summary>
    internal long Ticks => _ticks;

    /// <summary>Returns the result, or throws: the callback's exception with its original stack, or a fresh rejection exception.</summary>
    /// <returns>The result.</returns>
    public TResult GetResultOrThrow()
    {
        if (_rejection != RejectionKind.None)
        {
            throw CreateRejectionException();
        }

        if (_exception is not null)
        {
            ExceptionDispatchInfo.Throw(_exception);
        }

        return _result!;
    }

    /// <summary>Throws if the outcome holds an exception or a rejection; does nothing for a success.</summary>
    public void ThrowIfException()
    {
        if (!IsSuccess)
        {
            GetResultOrThrow();
        }
    }

    /// <inheritdoc />
    public override string ToString() => _rejection != RejectionKind.None
        ? $"Rejected: {_rejection}"
        : _exception is not null ? _exception.Message : _result?.ToString() ?? string.Empty;

    /// <summary>Gets the result when there is one that is not the internal void marker.</summary>
    internal bool TryGetResult(out TResult? result)
    {
        if (IsSuccess && _result is not Pipeline.VoidResult)
        {
            result = _result;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>The same outcome typed as <see cref="object"/> (boxes a value-type result). Used by non-generic strategy options.</summary>
    internal Outcome<object> AsObjectOutcome() => new(_result, _exception, _ticks, _rejection);

    private ResilienceRejectedException CreateRejectionException()
    {
        var cause = _exception;
        TimeSpan? retryAfter = RetryAfter;
        return _rejection switch
        {
            RejectionKind.CircuitIsolated => cause is null
                ? new IsolatedCircuitException()
                : new IsolatedCircuitException("The circuit is manually held open and is not allowing calls.", cause),
            RejectionKind.CircuitOpen => (retryAfter, cause) switch
            {
                ({ } ra, { } c) => new BrokenCircuitException($"The circuit is now open and is not allowing calls. It can be retried after '{ra}'.", ra, c),
                ({ } ra, null) => new BrokenCircuitException(ra),
                (null, { } c) => new BrokenCircuitException("The circuit is now open and is not allowing calls.", c),
                _ => new BrokenCircuitException(),
            },
            RejectionKind.RateLimited => (retryAfter, cause) switch
            {
                ({ } ra, { } c) => new RateLimiterRejectedException($"The operation could not be executed because it was rejected by the rate limiter. It can be retried after '{ra}'.", ra, c),
                ({ } ra, null) => new RateLimiterRejectedException(ra),
                (null, { } c) => new RateLimiterRejectedException("The operation could not be executed because it was rejected by the rate limiter.", c),
                _ => new RateLimiterRejectedException(),
            },
            _ => CreateTimeoutException(cause),
        };
    }

    private TimeoutRejectedException CreateTimeoutException(Exception? cause)
    {
        if (_ticks < 0)
        {
            return cause is null ? new TimeoutRejectedException() : new TimeoutRejectedException("The operation didn't complete within the allowed timeout.", cause);
        }

        var timeout = TimeSpan.FromTicks(_ticks);
        var message = $"The operation didn't complete within the allowed timeout of '{timeout}'.";
        return cause is null ? new TimeoutRejectedException(message, timeout) : new TimeoutRejectedException(message, timeout, cause);
    }
}
