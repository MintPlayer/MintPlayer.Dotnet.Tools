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
    /// <remarks>
    /// <paramref name="because"/> comes before <paramref name="predicateExpression"/>, and
    /// <paramref name="becauseArgs"/> is a plain array rather than <c>params</c>, so that the
    /// caller-captured expression can stay last. The previous ordering put
    /// <paramref name="predicateExpression"/> second, which silently bound a positional
    /// <c>WithArgs(p, "my reason")</c> to it — the reason vanished from the message and the
    /// predicate text was replaced by it. Same shape as
    /// <see cref="Specialized.ExceptionAssertions{TException}.Where"/>, for the same reason.
    /// </remarks>
    public EventAssertions WithArgs<TArgs>(Func<TArgs, bool> predicate, string? because = null, object?[]? becauseArgs = null,
        [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var matches = Occurrences.Where(o => o.Parameters.OfType<TArgs>().Any(a => predicate(a))).ToArray();
        Assertion.For(subjectExpression).ForCondition(matches.Length > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to raise event {0} with arguments matching {1}{reason}, but no occurrence did.",
                eventName, string.IsNullOrWhiteSpace(predicateExpression) ? "the given predicate" : predicateExpression);
        return new(eventName, matches, subjectExpression);
    }

    /// <summary>
    /// Asserts at least one occurrence satisfies <em>every</em> predicate — each against some
    /// argument of its own type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point is that the predicates must hold for the <b>same</b> occurrence. Chaining two
    /// <see cref="WithArgs{TArgs}(Func{TArgs, bool}, string, object[], string)"/> calls does not say
    /// that: the first narrows to the occurrences it matched and the second runs against that
    /// narrowed set, which is nearly the same thing — but it passes when a single occurrence matched
    /// only the first predicate and a later one in the narrowed set matched the second, which is not
    /// what a reader takes a chain to mean when the predicates are about different arguments.
    /// </para>
    /// <para>
    /// The predicates are all over the same <typeparamref name="TArgs"/> and are matched
    /// independently, not positionally — an event's arguments arrive as a bag, and pinning a
    /// predicate to a position would break the moment a delegate's parameters are reordered without
    /// changing what the event means.
    /// </para>
    /// </remarks>
    public EventAssertions WithArgs<TArgs>(params Func<TArgs, bool>[] predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        if (predicates.Length == 0)
            throw new ArgumentException("At least one predicate is required; matching against none would pass for any occurrence.", nameof(predicates));

        var matches = Occurrences.Where(o =>
        {
            foreach (var predicate in predicates)
            {
                if (!o.Parameters.OfType<TArgs>().Any(a => predicate(a))) return false;
            }
            return true;
        }).ToArray();

        Assertion.For(subjectExpression).ForCondition(matches.Length > 0).BecauseOf(null, null)
            .FailWith("Expected {subject} to raise event {0} with a single occurrence satisfying all {1} argument predicate(s), but none did.",
                eventName, predicates.Length);
        return new(eventName, matches, subjectExpression);
    }

    /// <summary>Asserts the event was raised exactly <paramref name="expected"/> times.</summary>
    public EventAssertions Times(int expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expected);
        Assertion.For(subjectExpression).ForCondition(Occurrences.Count == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to raise event {0} {1} time(s){reason}, but it was raised {2} time(s).",
                eventName, expected, Occurrences.Count);
        return this;
    }
}
