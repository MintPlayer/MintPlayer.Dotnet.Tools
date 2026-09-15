namespace MintPlayer.Assertions;

/// <summary>
/// How many times something must occur — <c>Exactly.Twice()</c>, <c>AtLeast.Once()</c> and friends.
/// </summary>
/// <remarks>
/// <para>
/// A <c>readonly struct</c>, not a class hierarchy. FluentAssertions models this as an abstract
/// class with a derived type per comparison, which allocates one object per call site — small, but
/// paid on the passing path of every occurrence assertion, which this library's boundary does not
/// permit. A comparison kind plus a count carries exactly the same information in no allocation at
/// all.
/// </para>
/// <para>
/// The factories read as English at the call site: <c>Contain("x", Exactly.Twice())</c>.
/// </para>
/// </remarks>
public readonly struct OccurrenceConstraint
{
    private readonly Comparison comparison;
    private readonly int expected;

    private OccurrenceConstraint(Comparison comparison, int expected)
    {
        this.comparison = comparison;
        this.expected = expected;
    }

    private enum Comparison
    {
        /// <summary>Default(OccurrenceConstraint) must not silently behave like a real constraint.</summary>
        Unset = 0,
        AtLeast,
        AtMost,
        Exactly,
        MoreThan,
        LessThan,
    }

    internal static OccurrenceConstraint Create(int count, string kind) => kind switch
    {
        nameof(Comparison.AtLeast) => new(Comparison.AtLeast, count),
        nameof(Comparison.AtMost) => new(Comparison.AtMost, count),
        nameof(Comparison.Exactly) => new(Comparison.Exactly, count),
        nameof(Comparison.MoreThan) => new(Comparison.MoreThan, count),
        nameof(Comparison.LessThan) => new(Comparison.LessThan, count),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown occurrence comparison."),
    };

    /// <summary>Whether <paramref name="actual"/> satisfies this constraint.</summary>
    internal bool IsSatisfiedBy(int actual) => comparison switch
    {
        Comparison.AtLeast => actual >= expected,
        Comparison.AtMost => actual <= expected,
        Comparison.Exactly => actual == expected,
        Comparison.MoreThan => actual > expected,
        Comparison.LessThan => actual < expected,
        _ => throw new InvalidOperationException(
            "An OccurrenceConstraint was never given a comparison. Build one with Exactly, AtLeast, AtMost, MoreThan or LessThan rather than using default(OccurrenceConstraint)."),
    };

    /// <summary>How this constraint reads in a failure message, e.g. "exactly 2 time(s)".</summary>
    internal string Describe() => comparison switch
    {
        Comparison.AtLeast => $"at least {expected} time(s)",
        Comparison.AtMost => $"at most {expected} time(s)",
        Comparison.Exactly => $"exactly {expected} time(s)",
        Comparison.MoreThan => $"more than {expected} time(s)",
        Comparison.LessThan => $"fewer than {expected} time(s)",
        _ => "an unspecified number of times",
    };

    /// <inheritdoc />
    public override string ToString() => Describe();
}

/// <summary>At least this many occurrences.</summary>
public static class AtLeast
{
    public static OccurrenceConstraint Once() => Times(1);
    public static OccurrenceConstraint Twice() => Times(2);
    public static OccurrenceConstraint Thrice() => Times(3);
    public static OccurrenceConstraint Times(int count) => OccurrenceConstraint.Create(Guard(count), nameof(AtLeast));

    private static int Guard(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count;
    }
}

/// <summary>At most this many occurrences.</summary>
public static class AtMost
{
    public static OccurrenceConstraint Once() => Times(1);
    public static OccurrenceConstraint Twice() => Times(2);
    public static OccurrenceConstraint Thrice() => Times(3);
    public static OccurrenceConstraint Times(int count) => OccurrenceConstraint.Create(Guard(count), nameof(AtMost));

    private static int Guard(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count;
    }
}

/// <summary>Exactly this many occurrences.</summary>
public static class Exactly
{
    public static OccurrenceConstraint Once() => Times(1);
    public static OccurrenceConstraint Twice() => Times(2);
    public static OccurrenceConstraint Thrice() => Times(3);
    public static OccurrenceConstraint Times(int count) => OccurrenceConstraint.Create(Guard(count), nameof(Exactly));

    private static int Guard(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count;
    }
}

/// <summary>More than this many occurrences.</summary>
public static class MoreThan
{
    public static OccurrenceConstraint Once() => Times(1);
    public static OccurrenceConstraint Twice() => Times(2);
    public static OccurrenceConstraint Thrice() => Times(3);
    public static OccurrenceConstraint Times(int count) => OccurrenceConstraint.Create(Guard(count), nameof(MoreThan));

    private static int Guard(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count;
    }
}

/// <summary>Fewer than this many occurrences.</summary>
public static class LessThan
{
    public static OccurrenceConstraint Once() => Times(1);
    public static OccurrenceConstraint Twice() => Times(2);
    public static OccurrenceConstraint Thrice() => Times(3);
    public static OccurrenceConstraint Times(int count) => OccurrenceConstraint.Create(Guard(count), nameof(LessThan));

    private static int Guard(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count;
    }
}
