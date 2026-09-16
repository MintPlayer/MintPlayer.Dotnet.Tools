using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Events;

/// <summary>
/// Records every occurrence of the subject's public events for the lifetime of the monitor and
/// exposes the assertion surface over them (<see cref="Raise"/>, <see cref="NotRaise"/>,
/// <see cref="RaisePropertyChangeFor{TProperty}"/>, …). Created via
/// <c>subject.Monitor()</c>; dispose to unsubscribe.
/// </summary>
/// <remarks>
/// Supported event shapes: any void delegate whose Invoke signature is
/// <c>(object sender, TArgs args)</c>-shaped — <see cref="EventHandler"/>,
/// <see cref="EventHandler{TEventArgs}"/>, <see cref="PropertyChangedEventHandler"/> and custom
/// two-parameter void delegates — plus zero-parameter void delegates (e.g. <see cref="Action"/>).
/// Events of any other shape are skipped silently and listed in <see cref="UnmonitoredEvents"/>.
/// </remarks>
public sealed class EventMonitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicEvents)] T> : IDisposable
    where T : class
{
    internal const string DynamicCodeMessage =
        "Monitoring an event whose args parameter is a value type requires MakeGenericMethod over that value type, " +
        "which needs runtime code generation. Under Native AOT such events land in UnmonitoredEvents instead of being " +
        "recorded; events with reference-type args (EventHandler, EventHandler<TArgs> with a class TArgs, " +
        "PropertyChangedEventHandler) and parameterless events keep working.";

    private static readonly MethodInfo GenericHandleMethod = typeof(EventRecorder).GetMethod(nameof(EventRecorder.Handle))!;
    private static readonly MethodInfo ParameterlessHandleMethod = typeof(EventRecorder).GetMethod(nameof(EventRecorder.HandleParameterless))!;

    private readonly object gate = new();
    private readonly List<RecordedEvent> occurredEvents = [];
    private readonly List<(EventInfo Event, Delegate Handler)> subscriptions = [];
    private readonly HashSet<string> monitoredEventNames = [];
    private readonly List<string> unmonitoredEvents = [];
    /// <summary>
    /// The subject, held weakly.
    /// </summary>
    /// <remarks>
    /// Weak on purpose. The subscription runs the other way — the subject holds the handler, which
    /// holds the recorder, which holds this monitor — so a strong field here formed a cycle in which
    /// anything holding the monitor kept the subject alive. That is invisible in a <c>using</c>
    /// block and very visible in a fixture that keeps a monitor in a field, where it silently
    /// defeats any test about the subject being collected. Nothing is lost: while the subject is
    /// alive the subscription keeps this monitor alive too, and once it is gone there is nothing
    /// left to unsubscribe from.
    /// </remarks>
    private readonly WeakReference<T> subject;
    private readonly string subjectExpression;
    private readonly EventMonitorOptions options;
    private int sequence;
    private bool disposed;

    /// <summary>Subscribes to all supported public events of <paramref name="subject"/>.</summary>
    [RequiresDynamicCode(DynamicCodeMessage)]
    public EventMonitor(T subject, string? subjectExpression = null)
        : this(subject, EventMonitorOptions.Default, subjectExpression) { }

    /// <summary>Subscribes to the events of <paramref name="subject"/> that <paramref name="options"/> allows.</summary>
    [RequiresDynamicCode(DynamicCodeMessage)]
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The generic method being closed is our own EventRecorder.Handle<TArgs>, whose generic parameter carries no trimming annotations.")]
    // IL2090 is the GetInterfaces() call on typeof(T); IL2075 is GetEvents() on each interface the
    // loop yields. Both ids are checked against the build rather than guessed — the v1 plan records
    // two suppressions that cited the wrong id, which meant they suppressed nothing and the
    // AOT-clean claim was not being enforced at all.
    [UnconditionalSuppressMessage("Trimming", "IL2090",
        Justification = "Interface events are opt-in and additive: an interface whose events were trimmed simply contributes none, which is the default behaviour.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Same opt-in path; a trimmed interface contributes no events and the monitor behaves as it does by default.")]
    public EventMonitor(T subject, EventMonitorOptions options, string? subjectExpression = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(options);
        this.subject = new(subject);
        this.options = options;
        this.subjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "subject" : subjectExpression!;

        Subscribe(subject, typeof(T).GetEvents(BindingFlags.Public | BindingFlags.Instance));

        if (options.IncludeInterfaceEvents)
        {
            foreach (var contract in typeof(T).GetInterfaces())
            {
                Subscribe(subject, contract.GetEvents());
            }
        }

        if (options.ThrowOnUnmonitoredEvents && unmonitoredEvents.Count > 0)
        {
            throw new InvalidOperationException(
                $"Type {typeof(T).Name} exposes event(s) that cannot be monitored: {string.Join(", ", unmonitoredEvents)}. "
                + "They would be silently absent from every assertion, so ThrowOnUnmonitoredEvents reports them here instead.");
        }
    }

    [RequiresDynamicCode(DynamicCodeMessage)]
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The generic method being closed is our own EventRecorder.Handle<TArgs>, whose generic parameter carries no trimming annotations.")]
    private void Subscribe(T instance, EventInfo[] events)
    {
        foreach (var evt in events)
        {
            // An event already monitored under this name — an implicitly implemented interface event
            // reached through both the class and the interface — would record every occurrence twice.
            if (monitoredEventNames.Contains(evt.Name)) continue;
            if (options.EventFilter is { } filter && !filter(evt)) continue;

            var handlerType = evt.EventHandlerType;
            var invoke = handlerType is null ? null : GetInvokeMethod(handlerType);
            var parameters = invoke?.GetParameters() ?? [];

            if (handlerType is null || invoke is null || invoke.ReturnType != typeof(void)
                || parameters.Any(p => p.ParameterType.IsByRef)
                || (parameters.Length != 0 && (parameters.Length != 2 || parameters[0].ParameterType.IsValueType)))
            {
                if (!unmonitoredEvents.Contains(evt.Name)) unmonitoredEvents.Add(evt.Name);
                continue;
            }

            var recorder = new EventRecorder(this, evt.Name);
            try
            {
                var handler = parameters.Length == 0
                    ? Delegate.CreateDelegate(handlerType, recorder, ParameterlessHandleMethod)
                    : Delegate.CreateDelegate(handlerType, recorder, GenericHandleMethod.MakeGenericMethod(parameters[1].ParameterType));
                evt.AddEventHandler(instance, handler);
                subscriptions.Add((evt, handler));
                monitoredEventNames.Add(evt.Name);
                unmonitoredEvents.Remove(evt.Name);
            }
            catch (Exception)
            {
                // MakeGenericMethod over a value type can throw under Native AOT; an incompatible
                // delegate shape can make CreateDelegate throw. Either way the event is unsupported.
                if (!unmonitoredEvents.Contains(evt.Name)) unmonitoredEvents.Add(evt.Name);
            }
        }
    }

    /// <summary>Every recorded occurrence so far, in recording order (a snapshot; safe to enumerate while events fire).</summary>
    public IReadOnlyList<RecordedEvent> OccurredEvents
    {
        get { lock (gate) return [.. occurredEvents]; }
    }

    /// <summary>Public events on <typeparamref name="T"/> whose delegate shape is not supported and which are therefore not recorded.</summary>
    public IReadOnlyList<string> UnmonitoredEvents => unmonitoredEvents;

    /// <summary>
    /// The names of the events actually being recorded.
    /// </summary>
    /// <remarks>
    /// The other half of <see cref="UnmonitoredEvents"/>, and the one a test reaches for when an
    /// event it expected simply is not there: "never raised" and "never watched" produce the same
    /// failure and mean opposite things.
    /// </remarks>
    public IReadOnlyCollection<string> MonitoredEvents => monitoredEventNames;

    /// <summary>
    /// The occurrences recorded for <paramref name="eventName"/>, without asserting anything.
    /// </summary>
    /// <remarks>
    /// For the assertions <see cref="Raise"/> cannot express — an exact ordering across two
    /// different events, a count computed from the arguments. Returns an empty list for an event
    /// that was never raised, and still refuses an event that is not being monitored, because
    /// silently answering "none" there is how a test comes to assert nothing at all.
    /// </remarks>
    public IReadOnlyList<RecordedEvent> GetRecordingFor(string eventName)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventName);
        EnsureMonitored(eventName);
        return GetOccurrences(eventName);
    }

    /// <summary>
    /// Returns this monitor, so <c>monitor.Should().Raise(...)</c> compiles alongside the terse
    /// <c>monitor.Raise(...)</c>.
    /// </summary>
    /// <remarks>
    /// PRD §13's fifth open question, answered with "both". The terse form stays the one this
    /// library's own tests and README use, because the monitor <em>is</em> the subject and a
    /// <c>Should()</c> that returns itself adds a word and no meaning. But FluentAssertions spells it
    /// the other way, code gets ported, and refusing the spelling buys nothing — so it is an alias,
    /// not a second implementation, and cannot drift from what it aliases.
    /// </remarks>
    public EventMonitor<T> Should() => this;

    /// <summary>Discards all recorded occurrences (subscriptions stay active).</summary>
    public void Clear()
    {
        lock (gate)
        {
            occurredEvents.Clear();
            sequence = 0;
        }
    }

    /// <summary>Asserts the given event was raised at least once; returns <see cref="EventAssertions"/> over its occurrences for chaining.</summary>
    public EventAssertions Raise(string eventName, string? because = null, params object?[] becauseArgs)
    {
        EnsureMonitored(eventName);
        var occurrences = GetOccurrences(eventName);
        Assertion.For(subjectExpression).ForCondition(occurrences.Count > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to raise event {0}{reason}, but it was never raised.", eventName);
        return new(eventName, occurrences, subjectExpression);
    }

    /// <summary>Asserts the given event was never raised.</summary>
    public void NotRaise(string eventName, string? because = null, params object?[] becauseArgs)
    {
        EnsureMonitored(eventName);
        var occurrences = GetOccurrences(eventName);
        Assertion.For(subjectExpression).ForCondition(occurrences.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to raise event {0}{reason}, but it was raised {1} time(s).", eventName, occurrences.Count);
    }

    /// <summary>Asserts no monitored event was raised at all.</summary>
    /// <remarks>
    /// Cheaper to write and far better to read than one <see cref="NotRaise"/> per event, and it
    /// does not go stale: an event added to the type later is covered automatically, where a list of
    /// <c>NotRaise</c> calls quietly stops covering everything the moment the type grows.
    /// </remarks>
    public void NotRaiseAnyEvents(string? because = null, params object?[] becauseArgs)
    {
        var occurrences = OccurredEvents;
        Assertion.For(subjectExpression).ForCondition(occurrences.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to raise any events{reason}, but it raised {0}.", Describe(occurrences));
    }

    private static string Describe(IReadOnlyList<RecordedEvent> occurrences)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < occurrences.Count; i++)
        {
            var name = occurrences[i].EventName;
            counts[name] = counts.TryGetValue(name, out var n) ? n + 1 : 1;
        }
        return string.Join(", ", counts.Select(pair => $"{pair.Key} ({pair.Value}x)"));
    }

    /// <summary>
    /// Asserts an <see cref="INotifyPropertyChanged"/> subject raised PropertyChanged for the
    /// property named by <paramref name="propertyExpression"/> (a raise with a null or empty
    /// PropertyName counts, as it signals "all properties changed").
    /// </summary>
    public EventAssertions RaisePropertyChangeFor<TProperty>(Expression<Func<T, TProperty>> propertyExpression,
        string? because = null, params object?[] becauseArgs)
    {
        var propertyName = GetMemberName(propertyExpression);
        EnsureMonitored(nameof(INotifyPropertyChanged.PropertyChanged));
        var occurrences = GetPropertyChangeOccurrences(propertyName);
        Assertion.For(subjectExpression).ForCondition(occurrences.Count > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to raise PropertyChanged for property {0}{reason}, but it did not.", propertyName);
        return new(nameof(INotifyPropertyChanged.PropertyChanged), occurrences, subjectExpression);
    }

    /// <summary>Asserts PropertyChanged was never raised for the property named by <paramref name="propertyExpression"/>.</summary>
    public void NotRaisePropertyChangeFor<TProperty>(Expression<Func<T, TProperty>> propertyExpression,
        string? because = null, params object?[] becauseArgs)
    {
        var propertyName = GetMemberName(propertyExpression);
        EnsureMonitored(nameof(INotifyPropertyChanged.PropertyChanged));
        var occurrences = GetPropertyChangeOccurrences(propertyName);
        Assertion.For(subjectExpression).ForCondition(occurrences.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to raise PropertyChanged for property {0}{reason}, but it was raised {1} time(s).", propertyName, occurrences.Count);
    }

    /// <summary>Unsubscribes from all monitored events; recorded occurrences remain readable.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        // A collected subject has already taken every handler with it, so there is nothing to
        // unsubscribe from — and resurrecting it to say so would be worse than doing nothing.
        if (subject.TryGetTarget(out var instance))
        {
            foreach (var (evt, handler) in subscriptions)
            {
                try { evt.RemoveEventHandler(instance, handler); }
                catch { /* a throwing remove accessor must not prevent unsubscribing the rest */ }
            }
        }
        subscriptions.Clear();
    }

    internal void Record(string eventName, object? sender, object?[] parameters)
    {
        lock (gate)
        {
            occurredEvents.Add(new(eventName, sender, parameters, DateTime.UtcNow, sequence++));
        }
    }

    private IReadOnlyList<RecordedEvent> GetOccurrences(string eventName)
    {
        lock (gate) return [.. occurredEvents.Where(e => e.EventName == eventName)];
    }

    private IReadOnlyList<RecordedEvent> GetPropertyChangeOccurrences(string propertyName)
    {
        lock (gate)
        {
            return [.. occurredEvents.Where(e =>
                e.EventName == nameof(INotifyPropertyChanged.PropertyChanged) &&
                e.Parameters.OfType<PropertyChangedEventArgs>().Any(a =>
                    string.IsNullOrEmpty(a.PropertyName) || a.PropertyName == propertyName))];
        }
    }

    private void EnsureMonitored(string eventName)
    {
        if (monitoredEventNames.Contains(eventName)) return;
        throw new InvalidOperationException(unmonitoredEvents.Contains(eventName)
            ? $"Event \"{eventName}\" on type {typeof(T).Name} has an unsupported delegate shape and is not being monitored."
            : $"Type {typeof(T).Name} does not expose a public event named \"{eventName}\".");
    }

    private static string GetMemberName<TProperty>(Expression<Func<T, TProperty>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        var body = propertyExpression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;
        return body is MemberExpression member
            ? member.Member.Name
            : throw new ArgumentException("The expression must be a simple member access, e.g. x => x.Name.", nameof(propertyExpression));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Delegate types always preserve their Invoke method, so no annotation on handlerType is needed.")]
    private static MethodInfo? GetInvokeMethod(Type handlerType) => handlerType.GetMethod("Invoke");

    /// <summary>
    /// Per-event handler target. The open generic <see cref="Handle{TArgs}"/> is closed over the
    /// event's args type via MakeGenericMethod so any (object sender, TArgs args)-shaped delegate
    /// can bind to it; <see cref="HandleParameterless"/> serves zero-parameter delegates.
    /// </summary>
    private sealed class EventRecorder(EventMonitor<T> monitor, string eventName)
    {
        public void Handle<TArgs>(object? sender, TArgs e) => monitor.Record(eventName, sender, [e]);

        public void HandleParameterless() => monitor.Record(eventName, null, []);
    }
}
