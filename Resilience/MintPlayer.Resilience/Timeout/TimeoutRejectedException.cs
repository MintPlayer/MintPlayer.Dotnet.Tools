namespace MintPlayer.Resilience.Timeout;

/// <summary>Raised when a timeout strategy cancels a call that did not complete in time.</summary>
public class TimeoutRejectedException : ResilienceRejectedException
{
    private const string DefaultMessage = "The operation didn't complete within the allowed timeout.";

    /// <summary>Initializes a new instance with the default message.</summary>
    public TimeoutRejectedException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The message that describes the timeout.</param>
    public TimeoutRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and the cancellation that the timeout caused.</summary>
    /// <param name="message">The message that describes the timeout.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public TimeoutRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance for the given timeout.</summary>
    /// <param name="timeout">The timeout that elapsed.</param>
    public TimeoutRejectedException(TimeSpan timeout)
        : base(DefaultMessage) => Timeout = timeout;

    /// <summary>Initializes a new instance with a message, for the given timeout.</summary>
    /// <param name="message">The message that describes the timeout.</param>
    /// <param name="timeout">The timeout that elapsed.</param>
    public TimeoutRejectedException(string message, TimeSpan timeout)
        : base(message) => Timeout = timeout;

    /// <summary>Initializes a new instance with a message and a cause, for the given timeout.</summary>
    /// <param name="message">The message that describes the timeout.</param>
    /// <param name="timeout">The timeout that elapsed.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public TimeoutRejectedException(string message, TimeSpan timeout, Exception innerException)
        : base(message, innerException) => Timeout = timeout;

    /// <summary>Gets the timeout that elapsed, or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> when unknown.</summary>
    public TimeSpan Timeout { get; } = System.Threading.Timeout.InfiniteTimeSpan;

    /// <inheritdoc />
    public override RejectionKind Kind => RejectionKind.Timeout;
}
