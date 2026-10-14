namespace MintPlayer.Resilience.CircuitBreaker;

/// <summary>Raised when a call is rejected because a circuit breaker is manually held open (isolated).</summary>
public class IsolatedCircuitException : BrokenCircuitException
{
    /// <summary>Initializes a new instance with the default message.</summary>
    public IsolatedCircuitException()
        : base("The circuit is manually held open and is not allowing calls.")
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    public IsolatedCircuitException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and an inner exception.</summary>
    /// <param name="message">The message that describes the rejection.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public IsolatedCircuitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <inheritdoc />
    public override RejectionKind Kind => RejectionKind.CircuitIsolated;
}
