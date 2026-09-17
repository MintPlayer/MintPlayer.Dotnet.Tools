namespace MintPlayer.Assertions.Collections;

/// <summary>
/// Set relations, positional lookups and consecutive-order assertions — the gaps that made a reader
/// reach back for LINQ mid-assertion.
/// </summary>
/// <remarks>
/// A second file rather than 400 more lines in the first, and a partial rather than an extension
/// class so these reach <c>Items</c> (the span) and <c>FailNull</c> like every other assertion here.
/// <para>
/// ⚠️ Every one of these is <b>own-type cost</b>: it runs only for the caller who invoked it, and
/// none of it touches reflection. That is the entry condition for this milestone, not a preference —
/// an assertion that reaches for <c>Type.GetProperty</c> to answer a question belongs with the
/// Types/MemberInfo family that is deliberately out of scope.
/// </para>
/// </remarks>
public partial class GenericCollectionAssertions<T>
{
    /// <summary>Asserts every item of <paramref name="expectedSubset"/> is also part of the collection.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeSupersetOf(IEnumerable<T> expectedSubset, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expectedSubset);
        var items = Items;
        if (Subject is null) return FailNull("to be a superset of the given subset", because, becauseArgs);

        var present = ToSet(items);
        List<T>? missing = null;
        foreach (var candidate in expectedSubset)
        {
            if (!present.Contains(candidate)) (missing ??= []).Add(candidate);
        }

        Assert().ForCondition(missing is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be a superset of {0}{reason}, but item(s) {1} are missing.", expectedSubset, missing);
        return new(this);
    }

    /// <summary>Asserts at least one item of <paramref name="unexpectedSubset"/> is missing from the collection.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeSupersetOf(IEnumerable<T> unexpectedSubset, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpectedSubset);
        var items = Items;
        if (Subject is null) return FailNull("not to be a superset of the given subset", because, becauseArgs);

        var present = ToSet(items);
        var isSuperset = true;
        foreach (var candidate in unexpectedSubset)
        {
            if (!present.Contains(candidate)) { isSuperset = false; break; }
        }

        Assert().ForCondition(!isSuperset).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be a superset of {0}{reason}.", unexpectedSubset);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection is a subset of <paramref name="expectedSuperset"/> <em>and</em> that the
    /// superset holds at least one item the collection does not.
    /// </summary>
    /// <remarks>
    /// "Proper" is about the distinct values on each side, not the item counts: <c>[1, 1]</c> is a
    /// proper subset of <c>[1, 2]</c> even though both have two items. Comparing counts instead is
    /// the obvious implementation and is wrong for any collection with duplicates.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> BeProperSubsetOf(IEnumerable<T> expectedSuperset, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expectedSuperset);
        var items = Items;
        if (Subject is null) return FailNull("to be a proper subset of the given superset", because, becauseArgs);

        var superset = new HashSet<T>(expectedSuperset);
        var mine = ToSet(items);

        Assert().ForCondition(mine.IsProperSubsetOf(superset)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be a proper subset of {0}{reason}, but it is not.", superset);
        return new(this);
    }

    /// <summary>
    /// Asserts <paramref name="expectedSubset"/> is a subset of the collection <em>and</em> that the
    /// collection holds at least one distinct value the subset does not.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeProperSupersetOf(IEnumerable<T> expectedSubset, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expectedSubset);
        var items = Items;
        if (Subject is null) return FailNull("to be a proper superset of the given subset", because, becauseArgs);

        var subset = new HashSet<T>(expectedSubset);
        var mine = ToSet(items);

        Assert().ForCondition(mine.IsProperSupersetOf(subset)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be a proper superset of {0}{reason}, but it is not.", subset);
        return new(this);
    }

    /// <summary>Asserts the item at <paramref name="index"/> equals <paramref name="expected"/>.</summary>
    /// <remarks>
    /// A negative index or one past the end is a failed assertion, not an exception: a test that
    /// indexes off the end of a collection is asserting something false about it, and an
    /// <see cref="ArgumentOutOfRangeException"/> would report that as an error rather than a failure.
    /// </remarks>
    public AndWhichConstraint<GenericCollectionAssertions<T>, T> HaveElementAt(int index, T expected, string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null)
        {
            FailNull($"to have element {Formatting.Formatter.Format(expected)} at index {index}", because, becauseArgs);
            return new(this, default!);
        }

        if (index < 0 || index >= items.Length)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to have element {0} at index {1}{reason}, but it has {2} item(s).", expected, index, items.Length);
            return new(this, default!);
        }

        var actual = items[index];
        Assert().ForCondition(EqualityComparer<T>.Default.Equals(actual, expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have element {0} at index {1}{reason}, but found {2}.", expected, index, actual);
        return new(this, actual);
    }

    /// <summary>Asserts <paramref name="expected"/> comes immediately before <paramref name="successor"/>.</summary>
    /// <remarks>Uses the first occurrence of <paramref name="successor"/>.</remarks>
    public AndConstraint<GenericCollectionAssertions<T>> HaveElementPreceding(T successor, T expected, string? because = null, params object?[] becauseArgs)
        => HaveNeighbour(successor, expected, preceding: true, because, becauseArgs);

    /// <summary>Asserts <paramref name="expected"/> comes immediately after <paramref name="predecessor"/>.</summary>
    /// <remarks>Uses the first occurrence of <paramref name="predecessor"/>.</remarks>
    public AndConstraint<GenericCollectionAssertions<T>> HaveElementSucceeding(T predecessor, T expected, string? because = null, params object?[] becauseArgs)
        => HaveNeighbour(predecessor, expected, preceding: false, because, becauseArgs);

    private AndConstraint<GenericCollectionAssertions<T>> HaveNeighbour(T anchor, T expected, bool preceding, string? because, object?[] becauseArgs)
    {
        // The relation words are baked into the template rather than passed as arguments, and no
        // placeholder index is reused.
        //
        // Both are FailWith rules and neither is enforced by the compiler. An argument goes through
        // the Formatter, so passing "preceding" would render it quoted, as a string value; and a
        // template that mentions {1} twice does not substitute it twice. This method got both wrong
        // on the first attempt and HaveElementPreceding_FailsAtTheStartOfTheCollection is what said
        // so — worth a comment, because the failure looked like a wrong assertion rather than a
        // wrong message.
        var relation = preceding ? "preceding" : "succeeding";
        var anchorRole = preceding ? "successor" : "predecessor";
        var edge = preceding ? "start" : "end";

        var items = Items;
        if (Subject is null) return FailNull($"to have element {Formatting.Formatter.Format(expected)} {relation} {Formatting.Formatter.Format(anchor)}", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var anchorIndex = -1;
        for (var i = 0; i < items.Length; i++)
        {
            if (comparer.Equals(items[i], anchor)) { anchorIndex = i; break; }
        }

        if (anchorIndex < 0)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} to have element {{0}} {relation} {{1}}{{reason}}, but that {anchorRole} was not found.", expected, anchor);
            return new(this);
        }

        var neighbourIndex = preceding ? anchorIndex - 1 : anchorIndex + 1;
        if (neighbourIndex < 0 || neighbourIndex >= items.Length)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} to have element {{0}} {relation} {{1}}{{reason}}, but that {anchorRole} is at the {edge} of the collection.", expected, anchor);
            return new(this);
        }

        var actual = items[neighbourIndex];
        Assert().ForCondition(comparer.Equals(actual, expected)).BecauseOf(because, becauseArgs)
            .FailWith($"Expected {{subject}} to have element {{0}} {relation} {{1}}{{reason}}, but found {{2}}.", expected, anchor, actual);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection contains the given items <em>adjacent and in order</em>, with nothing
    /// in between.
    /// </summary>
    /// <remarks>
    /// The strict sibling of <c>ContainInOrder</c>, which allows gaps. They are easy to confuse and
    /// the difference is the whole point of having both: <c>[1, 9, 2]</c> contains <c>[1, 2]</c> in
    /// order but not consecutively.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> ContainInConsecutiveOrder(IEnumerable<T> expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var items = Items;
        var expectedItems = expected as IReadOnlyList<T> ?? [.. expected];
        if (Subject is null) return FailNull($"to contain {Formatting.Formatter.Format(expectedItems)} in consecutive order", because, becauseArgs);

        Assert().ForCondition(IndexOfRun(items, expectedItems) >= 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0} in consecutive order{reason}, but it does not.", expectedItems);
        return new(this);
    }

    /// <summary>Asserts the collection does not contain the given items adjacent and in order.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContainInConsecutiveOrder(IEnumerable<T> unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var items = Items;
        var unexpectedItems = unexpected as IReadOnlyList<T> ?? [.. unexpected];
        if (Subject is null) return FailNull($"not to contain {Formatting.Formatter.Format(unexpectedItems)} in consecutive order", because, becauseArgs);

        var at = IndexOfRun(items, unexpectedItems);
        Assert().ForCondition(at < 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0} in consecutive order{reason}, but it does at index {1}.", unexpectedItems, at);
        return new(this);
    }

    /// <summary>Index of the first run matching <paramref name="run"/>, or -1. An empty run matches at 0.</summary>
    /// <remarks>
    /// Naive O(n·m) rather than a substring search. The runs an assertion is handed are a handful of
    /// items written by hand, so the setup cost of anything cleverer exceeds the search.
    /// </remarks>
    private static int IndexOfRun(ReadOnlySpan<T> items, IReadOnlyList<T> run)
    {
        if (run.Count == 0) return 0;
        if (run.Count > items.Length) return -1;

        var comparer = EqualityComparer<T>.Default;
        for (var start = 0; start <= items.Length - run.Count; start++)
        {
            var matched = true;
            for (var offset = 0; offset < run.Count; offset++)
            {
                if (comparer.Equals(items[start + offset], run[offset])) continue;
                matched = false;
                break;
            }
            if (matched) return start;
        }
        return -1;
    }

    /// <summary>The distinct items of the subject, for the set relations above.</summary>
    /// <remarks>
    /// Takes the span rather than the subject so it never re-enumerates a one-shot sequence, and so
    /// an array or List subject is not copied twice.
    /// </remarks>
    private static HashSet<T> ToSet(ReadOnlySpan<T> items)
    {
        var set = new HashSet<T>(items.Length);
        foreach (var item in items) set.Add(item);
        return set;
    }
}
