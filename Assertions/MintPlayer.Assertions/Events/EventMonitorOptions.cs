using System.Reflection;

namespace MintPlayer.Assertions.Events;

/// <summary>
/// What an <see cref="EventMonitor{T}"/> subscribes to, and how loudly it complains about what it
/// cannot.
/// </summary>
/// <remarks>
/// A record so a caller can change one thing without restating the rest:
/// <c>subject.Monitor(EventMonitorOptions.Default with { ThrowOnUnmonitoredEvents = true })</c>.
/// </remarks>
public sealed record EventMonitorOptions
{
    /// <summary>The defaults: exactly what the monitor did before any of this was configurable.</summary>
    public static EventMonitorOptions Default { get; } = new();

    /// <summary>
    /// Also subscribes to events declared on the interfaces <typeparamref name="T"/> implements.
    /// Off by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The case this exists for: a class implements <c>INotifyPropertyChanged.PropertyChanged</c>
    /// <em>explicitly</em>, so <c>typeof(T).GetEvents()</c> does not list it and the monitor silently
    /// records nothing. Every <c>RaisePropertyChangeFor</c> in the test then fails with "does not
    /// expose a public event named PropertyChanged", which is true and useless.
    /// </para>
    /// <para>
    /// Off by default because subscribing through the interface for an <em>implicitly</em>
    /// implemented event would record the same occurrence twice; the monitor skips names it already
    /// has, but only the caller knows whether reaching through the interface is what they meant.
    /// </para>
    /// </remarks>
    public bool IncludeInterfaceEvents { get; init; }

    /// <summary>
    /// Fails immediately when some event cannot be monitored, instead of listing it in
    /// <see cref="EventMonitor{T}.UnmonitoredEvents"/> and carrying on. Off by default.
    /// </summary>
    /// <remarks>
    /// The silent version is the right default — most types have an event or two the monitor cannot
    /// bind, and failing over an event the test never mentions would be noise. Turn it on when the
    /// test is specifically about events, where "it was never raised" and "it was never watched" look
    /// identical and mean opposite things.
    /// </remarks>
    public bool ThrowOnUnmonitoredEvents { get; init; }

    /// <summary>
    /// Restricts monitoring to the events this returns true for. Null (the default) monitors every
    /// event whose shape is supported.
    /// </summary>
    public Func<EventInfo, bool>? EventFilter { get; init; }
}
