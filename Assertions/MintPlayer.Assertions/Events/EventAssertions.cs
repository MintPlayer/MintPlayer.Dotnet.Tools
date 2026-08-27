using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Events;

/// <summary>
/// Assertions over the recorded occurrences of one event, returned by
/// <c>EventMonitor&lt;T&gt;.Raise(...)</c>. Each constraint narrows <see cref="Occurrences"/> to the
/// occurrences that satisfied it, so chained constraints describe a single matching occurrence.
/// </summary>
public sealed class EventAssertions
{
    private readonly string eventName;
    private readonly string subjectExpression;

    internal EventAssertions(string eventName, IReadOnlyList<RecordedEvent> occurrences, string subjectExpression)
    {
        this.eventName = eventName;
        Occurrences = occurrences;
        this.subjectExpression = subjectExpression;
    }

    /// <summary>The recorded occurrences this assertion currently describes.</summary>
    public IReadOnlyList<RecordedEvent> Occurrences { get; }

    /// <summary>Asserts at least one occurrence was raised with exactly (reference equality) the given sender.</summary>
    public EventAssertions WithSender(object expectedSender, string? because = null, params object?[] becauseArgs)
    {
        var matches = Occurrences.Where(o => ReferenceEquals(o.Sender, expectedSender)).ToArray();
        Assertion.For(subjectExpression).ForCondition(matches.Length > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to raise event {0} with sender {1}{reason}, but no occurrence had that sender.", eventName, expectedSender);
        return new(eventName, matches, subjectExpression);
    }

    /// <summary>Asserts at least one occurrence carries an argument of type <typeparamref name="TArgs"/> matching the predicate.</summary>
    public EventAssertions WithArgs<TArgs>(Func<TArgs, bool> predicate,
        [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null,
        string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var matches = Occurrences.Where(o => o.Parameters.OfType<TArgs>().Any(a => predicate(a))).ToArray();
        Assertion.For(subjectExpression).ForCondition(matches.Length > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to raise event {0} with arguments matching {1}{reason}, but no occurrence did.",
                eventName, string.IsNullOrWhiteSpace(predicateExpression) ? "the given predicate" : predicateExpression);
        return new(eventName, matches, subjectExpression);
    }
}
