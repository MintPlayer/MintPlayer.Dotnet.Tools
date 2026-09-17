namespace MintPlayer.Assertions;

/// <summary>
/// How many times something is expected to occur: <c>Exactly.Times(3)</c>, <c>AtLeast.Once()</c>,
/// <c>AtMost.Twice()</c>, <c>MoreThan.Times(2)</c>, <c>LessThan.Times(5)</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This is a separate parameter type, not an <c>int</c> overload, and that is the naming trap
/// in a slightly different disguise.</b> Every assertion in this library ends
/// <c>(…, string? because = null, params object?[] becauseArgs)</c>. An overload taking a bare count
/// would be fine; one taking a count and a comparison word would not, because the word is a string
/// and the <c>because</c> tail swallows anything string-shaped. A dedicated type makes the
/// comparison unmistakable at the call site and impossible to confuse with a reason.
/// </para>
/// <para>
/// A readonly struct because it is constructed at the call site of an assertion and read once. As a
/// class it would be one allocation on the passing path of every assertion that takes one.
/// </para>
/// </remarks>
public readonly struct OccurrenceConstraint
{
    private readonly ComparisonMode mode;
    private readonly int expected;

    internal OccurrenceConstraint(ComparisonMode mode, int expected)
    {
        this.mode = mode;
        this.expected = expected;
    }

    internal enum ComparisonMode
    {
        /// <summary>The default value is not a usable constraint; see <see cref="IsSatisfiedBy"/>.</summary>
        Unset = 0,
        Exactly,
        AtLeast,
        AtMost,
        MoreThan,
        LessThan,
    }

    /// <summary>Whether <paramref name="actual"/> occurrences satisfy this constraint.</summary>
    /// <remarks>
    /// ⚠️ <c>default(OccurrenceConstraint)</c> is rejected rather than silently treated as
    /// "exactly zero". A caller who writes <c>Contain(x, default)</c> has made a mistake, and the
    /// zero-value-is-a-valid-constraint reading would turn it into an assertion that quietly passes
    /// only when the item is absent — the opposite of what the call reads as.
    /// </remarks>
    internal bool IsSatisfiedBy(int actual) => mode switch
    {
        ComparisonMode.Exactly => actual == expected,
        ComparisonMode.AtLeast => actual >= expected,
        ComparisonMode.AtMost => actual <= expected,
        ComparisonMode.MoreThan => actual > expected,
        ComparisonMode.LessThan => actual < expected,
        _ => throw new InvalidOperationException(
            "An occurrence constraint was not specified. Use Exactly.Times(n), AtLeast.Times(n), "
            + "AtMost.Times(n), MoreThan.Times(n) or LessThan.Times(n)."),
    };

    /// <summary>Renders as the phrase that reads naturally inside a failure message.</summary>
    public override string ToString() => mode switch
    {
        ComparisonMode.Exactly => $"exactly {Times(expected)}",
        ComparisonMode.AtLeast => $"at least {Times(expected)}",
        ComparisonMode.AtMost => $"at most {Times(expected)}",
        ComparisonMode.MoreThan => $"more than {Times(expected)}",
        ComparisonMode.LessThan => $"less than {Times(expected)}",
        _ => "an unspecified number of times",
    };

    private static string Times(int count) => count == 1 ? "1 time" : $"{count} times";
}

/// <summary>Builds an "exactly n times" constraint.</summary>
public static class Exactly
{
    /// <summary>Exactly <paramref name="count"/> occurrences.</summary>
    public static OccurrenceConstraint Times(int count) => Occurrence.Build(OccurrenceConstraint.ComparisonMode.Exactly, count);

    /// <summary>Exactly once.</summary>
    public static OccurrenceConstraint Once() => Times(1);

    /// <summary>Exactly twice.</summary>
    public static OccurrenceConstraint Twice() => Times(2);

    /// <summary>Exactly three times.</summary>
    public static OccurrenceConstraint Thrice() => Times(3);
}

/// <summary>Builds an "at least n times" constraint.</summary>
public static class AtLeast
{
    /// <summary>At least <paramref name="count"/> occurrences.</summary>
    public static OccurrenceConstraint Times(int count) => Occurrence.Build(OccurrenceConstraint.ComparisonMode.AtLeast, count);

    /// <summary>At least once.</summary>
    public static OccurrenceConstraint Once() => Times(1);

    /// <summary>At least twice.</summary>
    public static OccurrenceConstraint Twice() => Times(2);

    /// <summary>At least three times.</summary>
    public static OccurrenceConstraint Thrice() => Times(3);
}

/// <summary>Builds an "at most n times" constraint.</summary>
public static class AtMost
{
    /// <summary>At most <paramref name="count"/> occurrences.</summary>
    public static OccurrenceConstraint Times(int count) => Occurrence.Build(OccurrenceConstraint.ComparisonMode.AtMost, count);

    /// <summary>At most once.</summary>
    public static OccurrenceConstraint Once() => Times(1);

    /// <summary>At most twice.</summary>
    public static OccurrenceConstraint Twice() => Times(2);

    /// <summary>At most three times.</summary>
    public static OccurrenceConstraint Thrice() => Times(3);
}

/// <summary>Builds a "more than n times" constraint.</summary>
public static class MoreThan
{
    /// <summary>More than <paramref name="count"/> occurrences.</summary>
    public static OccurrenceConstraint Times(int count) => Occurrence.Build(OccurrenceConstraint.ComparisonMode.MoreThan, count);

    /// <summary>More than once.</summary>
    public static OccurrenceConstraint Once() => Times(1);

    /// <summary>More than twice.</summary>
    public static OccurrenceConstraint Twice() => Times(2);
}

/// <summary>Builds a "less than n times" constraint.</summary>
public static class LessThan
{
    /// <summary>Fewer than <paramref name="count"/> occurrences.</summary>
    public static OccurrenceConstraint Times(int count) => Occurrence.Build(OccurrenceConstraint.ComparisonMode.LessThan, count);

    /// <summary>Fewer than twice.</summary>
    public static OccurrenceConstraint Twice() => Times(2);

    /// <summary>Fewer than three times.</summary>
    public static OccurrenceConstraint Thrice() => Times(3);
}

/// <summary>Shared validation for the five builders above.</summary>
internal static class Occurrence
{
    internal static OccurrenceConstraint Build(OccurrenceConstraint.ComparisonMode mode, int count)
    {
        // A negative count is a caller mistake rather than an assertion outcome: no collection can
        // contain an item -1 times, so every assertion using it would pass or fail vacuously.
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new(mode, count);
    }
}
