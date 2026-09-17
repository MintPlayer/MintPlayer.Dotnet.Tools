namespace MintPlayer.Assertions.Collections;

public partial class GenericCollectionAssertions<T>
{
    // ⚠️ Every assertion here builds its message ONLY on the failing branch, and that shape is the
    // whole reason these cost nothing.
    //
    // `Assert().ForCondition(x).FailWith($"… {occurrence} …", …)` looks equivalent and is not: the
    // interpolated template and the occurrence's ToString() are evaluated at the CALL SITE, before
    // FailWith can decide it has nothing to report. That cost 288 B/op on a passing
    // `Contain(item, Exactly.Twice())` — measured by AnOccurrenceConstraintAllocatesNothing, which is
    // the only thing that would ever have noticed.

    /// <summary>
    /// Asserts <paramref name="expected"/> occurs the number of times <paramref name="occurrence"/>
    /// describes: <c>items.Should().Contain(x, Exactly.Twice())</c>.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> Contain(T expected, OccurrenceConstraint occurrence, string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull($"to contain {Formatting.Formatter.Format(expected)} {occurrence}", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var count = 0;
        for (var i = 0; i < items.Length; i++)
        {
            if (comparer.Equals(items[i], expected)) count++;
        }

        if (!occurrence.IsSatisfiedBy(count))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} to contain {{0}} {occurrence}{{reason}}, but found it {{1}} time(s).", expected, count);
        }
        return new(this);
    }

    /// <summary>
    /// Asserts the number of items matching <paramref name="predicate"/> satisfies
    /// <paramref name="occurrence"/>.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> Contain(Func<T, bool> predicate, OccurrenceConstraint occurrence, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var items = Items;
        if (Subject is null) return FailNull($"to contain items matching the given predicate {occurrence}", because, becauseArgs);

        var count = 0;
        for (var i = 0; i < items.Length; i++)
        {
            if (predicate(items[i])) count++;
        }

        if (!occurrence.IsSatisfiedBy(count))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} to contain items matching the given predicate {occurrence}{{reason}}, but found {{0}} match(es).", count);
        }
        return new(this);
    }

    /// <summary>Asserts the item count satisfies <paramref name="occurrence"/>.</summary>
    /// <remarks>
    /// The comparison-specific siblings (<c>HaveCountGreaterThan</c> and friends) stay: they read
    /// better when the comparison is fixed, and this one reads better when it is a parameter.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCount(OccurrenceConstraint occurrence, string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull($"to contain items {occurrence}", because, becauseArgs);

        if (!occurrence.IsSatisfiedBy(items.Length))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} to contain items {occurrence}{{reason}}, but found {{0}}.", items.Length);
        }
        return new(this);
    }
}
