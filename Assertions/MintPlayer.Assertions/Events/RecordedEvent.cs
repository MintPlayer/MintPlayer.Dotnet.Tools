namespace MintPlayer.Assertions.Events;

/// <summary>
/// A single raised-event occurrence captured by an <see cref="EventMonitor{T}"/>:
/// which event fired, with what sender and arguments, and when.
/// </summary>
public sealed class RecordedEvent
{
    internal RecordedEvent(string eventName, object? sender, object?[] parameters, DateTime timestamp, int sequence)
    {
        EventName = eventName;
        Sender = sender;
        Parameters = parameters;
        Timestamp = timestamp;
        Sequence = sequence;
    }

    /// <summary>The name of the event that was raised.</summary>
    public string EventName { get; }

    /// <summary>The sender argument the event was raised with (null for parameterless events).</summary>
    public object? Sender { get; }

    /// <summary>The non-sender arguments the event was raised with (empty for parameterless events).</summary>
    public object?[] Parameters { get; }

    /// <summary>The UTC time at which the occurrence was recorded.</summary>
    public DateTime Timestamp { get; }

    /// <summary>Zero-based recording order across all monitored events, so relative ordering survives identical timestamps.</summary>
    public int Sequence { get; }
}
