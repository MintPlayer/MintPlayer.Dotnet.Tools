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
    private readonly T subject;
    private readonly string subjectExpression;
    private int sequence;
    private bool disposed;

    /// <summary>Subscribes to all supported public events of <paramref name="subject"/>.</summary>
    [RequiresDynamicCode(DynamicCodeMessage)]
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The generic method being closed is our own EventRecorder.Handle<TArgs>, whose generic parameter carries no trimming annotations.")]
    public EventMonitor(T subject, string? subjectExpression = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        this.subject = subject;
        this.subjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "subject" : subjectExpression!;

        foreach (var evt in typeof(T).GetEvents(BindingFlags.Public | BindingFlags.Instance))
        {
            var handlerType = evt.EventHandlerType;
            var invoke = handlerType is null ? null : GetInvokeMethod(handlerType);
            var parameters = invoke?.GetParameters() ?? [];

            if (handlerType is null || invoke is null || invoke.ReturnType != typeof(void)
                || parameters.Any(p => p.ParameterType.IsByRef)
                || (parameters.Length != 0 && (parameters.Length != 2 || parameters[0].ParameterType.IsValueType)))
            {
                unmonitoredEvents.Add(evt.Name);
                continue;
            }

            var recorder = new EventRecorder(this, evt.Name);
            try
            {
                var handler = parameters.Length == 0
                    ? Delegate.CreateDelegate(handlerType, recorder, ParameterlessHandleMethod)
                    : Delegate.CreateDelegate(handlerType, recorder, GenericHandleMethod.MakeGenericMethod(parameters[1].ParameterType));
                evt.AddEventHandler(subject, handler);
                subscriptions.Add((evt, handler));
                monitoredEventNames.Add(evt.Name);
            }
            catch (Exception)
            {
                // MakeGenericMethod over a value type can throw under Native AOT; an incompatible
                // delegate shape can make CreateDelegate throw. Either way the event is unsupported.
                unmonitoredEvents.Add(evt.Name);
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
        foreach (var (evt, handler) in subscriptions)
        {
            try { evt.RemoveEventHandler(subject, handler); }
            catch { /* a throwing remove accessor must not prevent unsubscribing the rest */ }
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
