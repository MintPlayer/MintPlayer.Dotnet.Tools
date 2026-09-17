namespace MintPlayer.Assertions.Primitives;

public partial class StringAssertions
{
    /// <summary>
    /// Asserts <paramref name="expected"/> occurs the number of times <paramref name="occurrence"/>
    /// describes: <c>text.Should().Contain("ab", Exactly.Twice())</c>.
    /// </summary>
    /// <remarks>
    /// Occurrences are counted <b>non-overlapping</b>, left to right: <c>"aaa"</c> contains
    /// <c>"aa"</c> once, not twice. Overlapping counts are defensible too, but they surprise more
    /// people than they help, and the two readings differ on exactly the inputs a test is likely to
    /// use to check the boundary.
    /// </remarks>
    public AndConstraint<StringAssertions> Contain(string expected, OccurrenceConstraint occurrence, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(expected);

        var count = CountOccurrences(Subject, expected);
        if (Subject is null || !occurrence.IsSatisfiedBy(count))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} to contain {{0}} {occurrence}{{reason}}, but found it {{1}} time(s).", expected, count);
        }
        return new(this);
    }

    /// <summary>
    /// Asserts <paramref name="expected"/> occurs the number of times <paramref name="occurrence"/>
    /// describes, ignoring casing.
    /// </summary>
    public AndConstraint<StringAssertions> ContainEquivalentOf(string expected, OccurrenceConstraint occurrence, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(expected);

        var count = CountOccurrences(Subject, expected, StringComparison.OrdinalIgnoreCase);
        if (Subject is null || !occurrence.IsSatisfiedBy(count))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} to contain the equivalent of {{0}} {occurrence}{{reason}}, but found it {{1}} time(s).", expected, count);
        }
        return new(this);
    }

    private static int CountOccurrences(string? subject, string needle, StringComparison comparison = StringComparison.Ordinal)
    {
        if (subject is null) return 0;

        var count = 0;
        var at = 0;
        while (at <= subject.Length - needle.Length)
        {
            var found = subject.IndexOf(needle, at, comparison);
            if (found < 0) break;
            count++;
            // Non-overlapping: resume after the match, not one character into it.
            at = found + needle.Length;
        }
        return count;
    }
}
