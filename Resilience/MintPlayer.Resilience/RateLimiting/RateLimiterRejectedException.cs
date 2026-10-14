namespace MintPlayer.Resilience.RateLimiting;

/// <summary>Raised when a rate or concurrency limiter refuses a permit.</summary>
public sealed class RateLimiterRejectedException : ResilienceRejectedException
{
    private const string DefaultMessage = "The operation could not be executed because it was rejected by the rate limiter.";

    /// <summary>Initializes a new instance with the default message.</summary>
    public RateLimiterRejectedException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Initializes a new instance that says when a permit may be available.</summary>
    /// <param name="retryAfter">How long until a permit may be available.</param>
    public RateLimiterRejectedException(TimeSpan retryAfter)
        : base($"{DefaultMessage} It can be retried after '{retryAfter}'.", retryAfter, null)
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    public RateLimiterRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and a retry-after hint.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    /// <param name="retryAfter">How long until a permit may be available.</param>
    public RateLimiterRejectedException(string message, TimeSpan retryAfter)
        : base(message, retryAfter, null)
    {
    }

    /// <summary>Initializes a new instance with a message and an inner exception.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    /// <param name="inner">The exception that caused this exception.</param>
    public RateLimiterRejectedException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Initializes a new instance with a message, a retry-after hint and an inner exception.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    /// <param name="retryAfter">How long until a permit may be available.</param>
    /// <param name="inner">The exception that caused this exception.</param>
    public RateLimiterRejectedException(string message, TimeSpan retryAfter, Exception inner)
        : base(message, retryAfter, inner)
    {
    }

    /// <inheritdoc />
    public override RejectionKind Kind => RejectionKind.RateLimited;
}
