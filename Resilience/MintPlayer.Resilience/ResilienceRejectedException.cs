namespace MintPlayer.Resilience;

/// <summary>
/// Base of every exception a strategy raises when it rejects a call: an open or isolated circuit, a
/// limiter refusal, or a timeout.
/// </summary>
/// <remarks>
/// A fresh instance is created for every throw and every read of <see cref="Outcome{TResult}.Exception"/>;
/// instances are never cached or shared, so their stack trace and <see cref="Exception.Data"/> always
/// belong to one call. Code that wants to avoid the exception altogether uses
/// <c>TryExecuteAsync</c> and tests <see cref="Outcome{TResult}.Rejection"/>.
/// </remarks>
public abstract class ResilienceRejectedException : Exception
{
    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    protected ResilienceRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and the exception that caused the rejection.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    /// <param name="innerException">The exception that caused the rejection.</param>
    protected ResilienceRejectedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance with a message, a retry-after hint and a cause.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    /// <param name="retryAfter">How long the caller should wait before trying again, when known.</param>
    /// <param name="innerException">The exception that caused the rejection.</param>
    protected ResilienceRejectedException(string message, TimeSpan? retryAfter, Exception? innerException)
        : base(message, innerException) => RetryAfter = retryAfter;

    /// <summary>Gets the kind of rejection this exception represents.</summary>
    public abstract RejectionKind Kind { get; }

    /// <summary>Gets how long the caller should wait before trying again, when the strategy knows it.</summary>
    public TimeSpan? RetryAfter { get; }
}
