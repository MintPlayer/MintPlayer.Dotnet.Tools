namespace MintPlayer.Resilience.CircuitBreaker;

/// <summary>Raised when a call is rejected because a circuit breaker is open.</summary>
public class BrokenCircuitException : ResilienceRejectedException
{
    private const string DefaultMessage = "The circuit is now open and is not allowing calls.";

    /// <summary>Initializes a new instance with the default message.</summary>
    public BrokenCircuitException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Initializes a new instance that says when the circuit may admit calls again.</summary>
    /// <param name="retryAfter">How long until the circuit may admit calls again.</param>
    public BrokenCircuitException(TimeSpan retryAfter)
        : base($"The circuit is now open and is not allowing calls. It can be retried after '{retryAfter}'.", retryAfter, null)
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    public BrokenCircuitException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and a retry-after hint.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    /// <param name="retryAfter">How long until the circuit may admit calls again.</param>
    public BrokenCircuitException(string message, TimeSpan retryAfter)
        : base(message, retryAfter, null)
    {
    }

    /// <summary>Initializes a new instance with a message and the exception that broke the circuit.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    /// <param name="inner">The exception that broke the circuit.</param>
    public BrokenCircuitException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Initializes a new instance with a message, a retry-after hint and the exception that broke the circuit.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    /// <param name="retryAfter">How long until the circuit may admit calls again.</param>
    /// <param name="inner">The exception that broke the circuit.</param>
    public BrokenCircuitException(string message, TimeSpan retryAfter, Exception inner)
        : base(message, retryAfter, inner)
    {
    }

    /// <inheritdoc />
    public override RejectionKind Kind => RejectionKind.CircuitOpen;
}
