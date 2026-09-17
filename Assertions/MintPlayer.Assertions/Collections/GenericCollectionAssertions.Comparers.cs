namespace MintPlayer.Assertions.Collections;

/// <summary>
/// The comparer overloads: the same membership and equality assertions, with equality supplied by
/// the caller instead of <see cref="EqualityComparer{T}.Default"/>.
/// </summary>
/// <remarks>
/// ⚠️ <b>These take an <see cref="IEqualityComparer{T}"/>, never a comparison lambda.</b> An overload
/// ending <c>(…, Func&lt;T, T, bool&gt; comparer, string? because = null, …)</c> would be the obvious
/// convenience and is exactly the naming trap: a lambda argument is fine, but it puts a new parameter
/// next to the <c>because</c>/<c>becauseArgs</c> tail every assertion here ends with, and the next
/// overload someone adds beside it will not be. A comparer is also the shape BCL collections already
/// use, so callers usually have one.
/// <para>
/// Each of these mirrors an existing assertion exactly, differing only in where equality comes from.
/// Where the default-comparer version has a documented behaviour — lazy failure lists, counting
/// rather than collecting — this one keeps it, because the two must not diverge.
/// </para>
/// </remarks>
public partial class GenericCollectionAssertions<T>
{
    /// <summary>Asserts the collection contains <paramref name="expected"/>, compared with <paramref name="comparer"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> Contain(T expected, IEqualityComparer<T> comparer, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        var items = Items;
        if (Subject is null) return FailNull($"to contain {Formatting.Formatter.Format(expected)}", because, becauseArgs);

        Assert().ForCondition(IndexOf(items, expected, comparer) >= 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection does not contain <paramref name="unexpected"/>, compared with <paramref name="comparer"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContain(T unexpected, IEqualityComparer<T> comparer, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        var items = Items;
        if (Subject is null) return FailNull($"not to contain {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);

        Assert().ForCondition(IndexOf(items, unexpected, comparer) < 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0}{reason}, but found it.", unexpected);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection equals <paramref name="expected"/> element-wise and in order, compared
    /// with <paramref name="comparer"/>.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> Equal(IEnumerable<T> expected, IEqualityComparer<T> comparer, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(comparer);
        var items = Items;
        if (Subject is null) return FailNull("to equal the given collection", because, becauseArgs);

        var expectedItems = expected as IReadOnlyList<T> ?? [.. expected];
        if (expectedItems.Count != items.Length)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to equal {0}{reason}, but it has {1} item(s) against {2}.",
                    expected, items.Length, expectedItems.Count);
            return new(this);
        }

        for (var i = 0; i < items.Length; i++)
        {
            if (comparer.Equals(items[i], expectedItems[i])) continue;

            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to equal {0}{reason}, but they differ at index {1}: expected {2}, found {3}.",
                    expected, i, expectedItems[i], items[i]);
            return new(this);
        }

        return new(this);
    }

    /// <summary>Asserts every item is also part of <paramref name="expectedSuperset"/>, compared with <paramref name="comparer"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeSubsetOf(IEnumerable<T> expectedSuperset, IEqualityComparer<T> comparer, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expectedSuperset);
        ArgumentNullException.ThrowIfNull(comparer);
        var items = Items;
        if (Subject is null) return FailNull("to be a subset of the given superset", because, becauseArgs);

        var superset = new HashSet<T>(expectedSuperset, comparer);
        // Lazy: a collection that is a subset is the passing case and must not allocate for it.
        List<T>? missing = null;
        for (var i = 0; i < items.Length; i++)
        {
            if (!superset.Contains(items[i])) (missing ??= []).Add(items[i]);
        }

        Assert().ForCondition(missing is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be a subset of {0}{reason}, but item(s) {1} are not part of the superset.", superset, missing);
        return new(this);
    }

    /// <summary>Asserts no two items are equal under <paramref name="comparer"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> OnlyHaveUniqueItems(IEqualityComparer<T> comparer, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        var items = Items;
        if (Subject is null) return FailNull("to only have unique items", because, becauseArgs);

        var seen = new HashSet<T>(comparer);
        List<T>? duplicates = null;
        for (var i = 0; i < items.Length; i++)
        {
            if (!seen.Add(items[i])) (duplicates ??= []).Add(items[i]);
        }

        Assert().ForCondition(duplicates is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to only have unique items{reason}, but found duplicate(s) {0}.", duplicates);
        return new(this);
    }

    private static int IndexOf(ReadOnlySpan<T> items, T value, IEqualityComparer<T> comparer)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (comparer.Equals(items[i], value)) return i;
        }
        return -1;
    }
}
