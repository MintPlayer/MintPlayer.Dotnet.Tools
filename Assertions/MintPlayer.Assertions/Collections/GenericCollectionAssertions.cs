using System.Runtime.InteropServices;
using MintPlayer.Assertions.Execution;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Collections;

/// <summary>
/// Assertions on any <see cref="IEnumerable{T}"/>: emptiness, counts, membership, ordering,
/// set relations and per-item inspection. The subject is materialized at most once per
/// assertions instance, so lazily-evaluated sequences are never enumerated multiple times.
/// </summary>
public class GenericCollectionAssertions<T> : ReferenceTypeAssertions<IEnumerable<T>, GenericCollectionAssertions<T>>
{
    private T[]? copy;
    private bool materialized;

    public GenericCollectionAssertions(IEnumerable<T>? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>
    /// The subject as a <see cref="ReadOnlySpan{T}"/>. Callers must rule out a null subject first —
    /// a span cannot represent one, and a null subject is a failure every assertion reports in its
    /// own words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A span rather than <c>IReadOnlyList&lt;T&gt;</c>, measured rather than assumed. Iterating the
    /// interface went through <c>IEnumerable&lt;T&gt;.GetEnumerator()</c>, whose return type is the
    /// interface <c>IEnumerator&lt;T&gt;</c>, so the underlying struct enumerator was <b>boxed</b> —
    /// one heap allocation per assertion, invisible in the source, because the loop reads identically
    /// whether the static type is concrete or not.
    /// </para>
    /// <para>
    /// Indexing the interface instead fixes the allocation and costs time: every <c>this[i]</c> and
    /// every <c>Count</c> is an interface dispatch the JIT cannot inline. Measured on 100k loops over
    /// 8 items, the naive indexed loop ran ~1.4x SLOWER than the boxed foreach it replaced. A span
    /// has no dispatch at all — its enumerator is a ref struct that inlines, and bounds checks go
    /// against the span's own length — and came out ~2x faster than the boxed foreach for a List
    /// subject and ~7x faster for an array.
    /// </para>
    /// <para>
    /// The array case is checked first and matters: an array is not a <c>List&lt;T&gt;</c>, so
    /// testing for the list alone silently drops every <c>T[]</c> subject onto the slow path — and
    /// arrays are what test code writes.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="CollectionsMarshal.AsSpan"/> hands back the list's own backing array. The span
    /// is invalidated if the list is resized. That is safe here only because no assertion mutates its
    /// subject; it would not be safe in general.
    /// </para>
    /// <para>
    /// Anything that is not already contiguous is copied once and cached, which is the case that
    /// forced materialisation in the first place: a lazily-evaluated sequence must not be enumerated
    /// once per assertion in a chain, and may not be replayable at all.
    /// </para>
    /// <para>
    /// For an already-contiguous subject this is a live view, not a snapshot — mutating the
    /// collection midway through a chained assertion is visible to the later links. That is the more
    /// honest reading: it IS the collection.
    /// </para>
    /// </remarks>
    private ReadOnlySpan<T> Items
    {
        get
        {
            // Contiguous already: no copy, no cache, no dispatch.
            if (Subject is T[] array) return array;
            if (Subject is List<T> list) return CollectionsMarshal.AsSpan(list);

            if (!materialized)
            {
                copy = Subject is null ? null : [.. Subject];
                materialized = true;
            }

            return copy;
        }
    }

    private AndConstraint<GenericCollectionAssertions<T>> FailNull(string expectation, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} " + expectation + "{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the collection contains no items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeEmpty(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("to be empty", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be empty{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains at least one item.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeEmpty(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("not to be empty", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be empty{reason}.");
        return new(this);
    }

    /// <summary>
    /// Asserts the collection is neither null nor empty — the collection counterpart of the string
    /// assertion of the same name.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeNullOrEmpty(string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        Assert().ForCondition(Subject is not null && items.Length > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be null or empty{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the collection is either null or empty.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeNullOrEmpty(string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        Assert().ForCondition(Subject is null || items.Length == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be null or empty{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains exactly <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCount(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to contain {expected} item(s)", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0} item(s){reason}, but found {1}: {2}.", expected, items.Length, Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection does not contain exactly <paramref name="unexpected"/> items. Says
    /// nothing about whether the real count is higher or lower — reach for
    /// <see cref="HaveCountGreaterThan"/> or <see cref="HaveCountLessThan"/> when the direction is
    /// what actually matters, since those produce a far more useful failure message.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotHaveCount(int unexpected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"not to contain {unexpected} item(s)", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0} item(s){reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection's count matches the given predicate.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCount(Func<int, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (Subject is null) return FailNull("to have a count matching the given predicate", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(predicate(items.Length)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a count matching the given predicate{reason}, but count is {0}: {1}.", items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains more than <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCountGreaterThan(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to contain more than {expected} item(s)", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain more than {0} item(s){reason}, but found {1}: {2}.", expected, items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains at least <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCountGreaterThanOrEqualTo(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to contain at least {expected} item(s)", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain at least {0} item(s){reason}, but found {1}: {2}.", expected, items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains fewer than <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCountLessThan(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to contain fewer than {expected} item(s)", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length < expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain fewer than {0} item(s){reason}, but found {1}: {2}.", expected, items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains at most <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCountLessThanOrEqualTo(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to contain at most {expected} item(s)", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length <= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain at most {0} item(s){reason}, but found {1}: {2}.", expected, items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection has the same number of items as <paramref name="otherCollection"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveSameCount(System.Collections.IEnumerable otherCollection, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(otherCollection);
        if (Subject is null) return FailNull("to have the same count as the other collection", because, becauseArgs);
        var items = Items;

        var expectedCount = Count(otherCollection);
        Assert().ForCondition(items.Length == expectedCount).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have {0} item(s), the same count as the other collection{reason}, but found {1}: {2}.", expectedCount, items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection does not have the same number of items as <paramref name="otherCollection"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotHaveSameCount(System.Collections.IEnumerable otherCollection, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(otherCollection);
        if (Subject is null) return FailNull("not to have the same count as the other collection", because, becauseArgs);
        var items = Items;

        var unexpectedCount = Count(otherCollection);
        Assert().ForCondition(items.Length != unexpectedCount).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have {0} item(s), the same count as the other collection{reason}.", unexpectedCount);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection contains exactly one item, and exposes it via <c>Which</c> — the
    /// equivalent of xUnit's <c>Assert.Single(collection)</c>.
    /// </summary>
    /// <remarks>
    /// To assert only that the one item equals a value, <see cref="Equal(T[])"/> says it in one
    /// call: <c>found.Should().Equal(path)</c> rather than
    /// <c>found.Should().ContainSingle().Which.Should().Be(path)</c>. There is deliberately no
    /// <c>ContainSingle(T expected)</c> overload: for a collection of strings it would be
    /// ambiguous with the <paramref name="because"/> parameter here.
    /// </remarks>
    public AndWhichConstraint<GenericCollectionAssertions<T>, T> ContainSingle(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null)
        {
            FailNull("to contain a single item", because, becauseArgs);
            return new(this, default!);
        }

        var items = Items;

        Assert().ForCondition(items.Length == 1).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain a single item{reason}, but found {0}: {1}.", items.Length, Subject);
        return new(this, items.Length == 1 ? items[0] : default!);
    }

    /// <summary>Asserts exactly one item matches the predicate, and exposes it via Which.</summary>
    public AndWhichConstraint<GenericCollectionAssertions<T>, T> ContainSingle(Func<T, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (Subject is null)
        {
            FailNull("to contain a single item matching the given predicate", because, becauseArgs);
            return new(this, default!);
        }

        var items = Items;

        var matches = new List<T>();
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (predicate(item)) matches.Add(item);
        }

        Assert().ForCondition(matches.Count == 1).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain a single item matching the given predicate{reason}, but found {0}: {1}.", matches.Count, matches);
        return new(this, matches.Count >= 1 ? matches[0] : default!);
    }

    /// <summary>
    /// Asserts the collection does not contain exactly one item — it is either empty or holds two
    /// or more.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ContainSingle(string?, object?[])"/> this returns a plain
    /// <see cref="AndConstraint{T}"/> rather than an <c>AndWhichConstraint</c>: when the assertion
    /// succeeds there is by definition no single item to hand back through <c>Which</c>.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> NotContainSingle(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("not to contain a single item", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length != 1).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain a single item{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the number of items matching the predicate is anything but exactly one — zero
    /// matches satisfies this just as well as three do. To assert that <em>no</em> item matches,
    /// use <see cref="NotContain(Func{T, bool}, string?, object?[])"/> instead.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContainSingle(Func<T, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (Subject is null) return FailNull("not to contain a single item matching the given predicate", because, becauseArgs);
        var items = Items;

        var matches = new List<T>();
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (predicate(item)) matches.Add(item);
        }

        Assert().ForCondition(matches.Count != 1).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain a single item matching the given predicate{reason}, but found {0}.", matches);
        return new(this);
    }

    /// <summary>Asserts the collection contains the given item.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> Contain(T expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to contain {Formatting.Formatter.Format(expected)}", because, becauseArgs);
        var items = Items;

        // Indexed, not foreach. `items` is typed IReadOnlyList<T>, so foreach iterates through the
        // INTERFACE and boxes the underlying struct enumerator — one heap allocation per assertion,
        // on the passing path. Indexing costs an interface call and allocates nothing.
        var comparer = EqualityComparer<T>.Default;
        var found = false;
        for (var i = 0; i < items.Length; i++)
        {
            if (comparer.Equals(items[i], expected)) { found = true; break; }
        }

        Assert().ForCondition(found).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts at least one item matches the predicate, and exposes the first match via Which.</summary>
    public AndWhichConstraint<GenericCollectionAssertions<T>, T> Contain(Func<T, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (Subject is null)
        {
            FailNull("to contain an item matching the given predicate", because, becauseArgs);
            return new(this, default!);
        }

        var items = Items;

        var found = false;
        T match = default!;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (predicate(item)) { found = true; match = item; break; }
        }

        Assert().ForCondition(found).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain an item matching the given predicate{reason}, but found {0}.", Subject);
        return new(this, match);
    }

    /// <summary>Asserts the collection does not contain the given item.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContain(T unexpected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"not to contain {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);
        var items = Items;

        // Indexed: foreach over the IReadOnlyList<T> interface boxes the struct enumerator.
        var comparer = EqualityComparer<T>.Default;
        var found = false;
        for (var i = 0; i < items.Length; i++)
        {
            if (comparer.Equals(items[i], unexpected)) { found = true; break; }
        }

        Assert().ForCondition(!found).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts no item matches the predicate.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContain(Func<T, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (Subject is null) return FailNull("not to contain an item matching the given predicate", because, becauseArgs);
        var items = Items;

        var matches = new List<T>();
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (predicate(item)) matches.Add(item);
        }

        Assert().ForCondition(matches.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain an item matching the given predicate{reason}, but found {0}.", matches);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection contains the given items in the given order, allowing other items
    /// in between (a subsequence match).
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> ContainInOrder(params T[] expected)
        => ContainInOrder((IEnumerable<T>)expected, null);

    /// <summary>
    /// Asserts the collection contains the given items in the given order, allowing other items
    /// in between (a subsequence match).
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> ContainInOrder(IEnumerable<T> expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var items = Items;
        var expectedItems = Spans.ListFrom(expected);
        if (Subject is null) return FailNull($"to contain {Formatting.Formatter.Format(expectedItems)} in order", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var position = 0;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (position < expectedItems.Count && comparer.Equals(item, expectedItems[position]))
                position++;
        }

        if (position < expectedItems.Count)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to contain {0} in order{reason}, but {1} (expected item {2}) was not found in that order in {3}.",
                    expectedItems, expectedItems[position], position, Subject);
        }
        return new(this);
    }

    /// <summary>
    /// Asserts the given items do <em>not</em> appear in the collection in the given relative
    /// order. They may all be present — as long as at least one of them is missing or comes out of
    /// sequence, this succeeds.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContainInOrder(params T[] unexpected)
        => NotContainInOrder((IEnumerable<T>)unexpected, null);

    /// <summary>
    /// Asserts the given items do <em>not</em> appear in the collection in the given relative
    /// order (a subsequence match), allowing other items in between.
    /// </summary>
    /// <remarks>
    /// An empty <paramref name="unexpected"/> sequence is vacuously contained in order by every
    /// collection, so this assertion always fails for one — the mirror image of
    /// <see cref="ContainInOrder(IEnumerable{T}, string?, object?[])"/> always succeeding.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> NotContainInOrder(IEnumerable<T> unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var items = Items;
        var unexpectedItems = Spans.ListFrom(unexpected);
        if (Subject is null) return FailNull($"not to contain {Formatting.Formatter.Format(unexpectedItems)} in order", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var position = 0;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (position < unexpectedItems.Count && comparer.Equals(item, unexpectedItems[position]))
                position++;
        }

        Assert().ForCondition(position < unexpectedItems.Count).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0} in order{reason}, but found {1}.", unexpectedItems, Subject);
        return new(this);
    }

    /// <summary>Asserts every item matches the predicate.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> OnlyContain(Func<T, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (Subject is null) return FailNull("to only contain items matching the given predicate", because, becauseArgs);
        var items = Items;

        var mismatches = new List<T>();
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!predicate(item)) mismatches.Add(item);
        }

        Assert().ForCondition(mismatches.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to only contain items matching the given predicate{reason}, but {0} did not.", mismatches);
        return new(this);
    }

    /// <summary>
    /// Asserts at least one item does <em>not</em> match the predicate — the exact logical negation
    /// of <see cref="OnlyContain"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately <em>not</em> "contains none of the matching items"; that assertion is
    /// <see cref="NotContain(Func{T, bool}, string?, object?[])"/>. A collection holding a mix of
    /// matching and non-matching items satisfies this one, because it is not true that it contains
    /// <em>only</em> matching items.
    /// </para>
    /// <para>
    /// Because <see cref="OnlyContain"/> is a universally quantified claim, it holds vacuously for
    /// an empty collection — so this negation fails for an empty collection. If the intent is
    /// really "no item matches", say that with <c>NotContain(predicate)</c>, which passes on empty.
    /// </para>
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> NotOnlyContain(Func<T, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (Subject is null) return FailNull("not to only contain items matching the given predicate", because, becauseArgs);
        var items = Items;

        var allMatch = true;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!predicate(item)) { allMatch = false; break; }
        }

        Assert().ForCondition(!allMatch).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to only contain items matching the given predicate{reason}, but all {0} item(s) did.", items.Length);
        return new(this);
    }

    /// <summary>Asserts the collection contains no duplicate items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> OnlyHaveUniqueItems(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("to only have unique items", because, becauseArgs);
        var items = Items;

        // Detect first, allocate second — and for a small collection, detect without allocating at
        // all. Two HashSets and a List were built on every call (464 bytes/op on five items) to
        // report duplicates that are almost never there.
        //
        // The pairwise scan is O(n^2) but allocation-free, which wins comfortably at the sizes that
        // dominate real assertions. Above the threshold the set earns its allocation.
        if (!HasDuplicates(items)) return new(this);

        var seen = new HashSet<T>();
        var duplicates = new HashSet<T>();
        var duplicatesInOrder = new List<T>();
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!seen.Add(item) && duplicates.Add(item))
                duplicatesInOrder.Add(item);
        }

        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to only have unique items{reason}, but found duplicate(s) {0}.", duplicatesInOrder);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection holds at least one duplicate item, using
    /// <see cref="EqualityComparer{T}.Default"/> — the negation of
    /// <see cref="OnlyHaveUniqueItems"/>.
    /// </summary>
    /// <remarks>
    /// Uniqueness holds vacuously for an empty or single-item collection, so this assertion fails
    /// for both. It is a genuine assertion about duplication, not a weaker "may contain
    /// duplicates".
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> NotOnlyHaveUniqueItems(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("not to only have unique items", because, becauseArgs);
        var items = Items;

        var seen = new HashSet<T>();
        var hasDuplicate = false;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!seen.Add(item)) { hasDuplicate = true; break; }
        }

        Assert().ForCondition(hasDuplicate).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to only have unique items{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection holds at least one <see langword="null"/> item — the positive mirror
    /// of <see cref="NotContainNulls"/>.
    /// </summary>
    /// <remarks>
    /// Like its negative counterpart there is no nullability constraint on <typeparamref name="T"/>,
    /// so this compiles for a non-nullable value type too — where it can never succeed, because no
    /// such item is ever <see langword="null"/>. The failure message reports the collection so that
    /// case is obvious rather than mysterious.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> ContainNulls(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("to contain <null> items", because, becauseArgs);
        var items = Items;

        var containsNull = false;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i] is null) { containsNull = true; break; }
        }

        Assert().ForCondition(containsNull).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain <null> items{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains no null items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContainNulls(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull("not to contain <null> items", because, becauseArgs);
        var items = Items;

        // Detect first, allocate second. The index list used to be built on every call — for a
        // collection with no nulls, which is the overwhelming majority, it was allocated, filled
        // with nothing and thrown away.
        var hasNull = false;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i] is null) { hasNull = true; break; }
        }

        if (!hasNull) return new(this);

        var nullIndexes = new List<int>();
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i] is null) nullIndexes.Add(i);
        }

        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to contain <null> items{reason}, but found <null> at index(es) {0}.", nullIndexes);
        return new(this);
    }

    /// <summary>Asserts the collection equals the given items pairwise, in order.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> Equal(params T[] expected)
        => Equal((IEnumerable<T>)expected, null);

    /// <summary>Asserts the collection equals the given collection pairwise, in order.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> Equal(IEnumerable<T> expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var items = Items;
        var expectedItems = Spans.ListFrom(expected);
        if (Subject is null) return FailNull($"to equal {Formatting.Formatter.Format(expectedItems)}", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var commonLength = Math.Min(items.Length, expectedItems.Count);
        for (var i = 0; i < commonLength; i++)
        {
            if (!comparer.Equals(items[i], expectedItems[i]))
            {
                Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                    .FailWith("Expected {subject} to equal {0}{reason}, but differs at index {1}: found {2} instead of {3}.",
                        expectedItems, i, items[i], expectedItems[i]);
                return new(this);
            }
        }

        Assert().ForCondition(items.Length == expectedItems.Count).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to equal {0}{reason}, but it contains {1} item(s) instead of {2}: {3}.",
                expectedItems, items.Length, expectedItems.Count, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection does not equal the given collection pairwise.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotEqual(IEnumerable<T> unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var items = Items;
        var unexpectedItems = Spans.ListFrom(unexpected);
        if (Subject is null) return FailNull($"not to equal {Formatting.Formatter.Format(unexpectedItems)}", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var equal = items.Length == unexpectedItems.Count;
        for (var i = 0; equal && i < items.Length; i++)
        {
            equal = comparer.Equals(items[i], unexpectedItems[i]);
        }

        Assert().ForCondition(!equal).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to equal {0}{reason}.", unexpectedItems);
        return new(this);
    }

    /// <summary>Asserts the collection starts with the given item.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> StartWith(T expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to start with {Formatting.Formatter.Format(expected)}", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to start with {0}{reason}, but the collection is empty.", expected)
            .ForCondition(items.Length == 0 || EqualityComparer<T>.Default.Equals(items[0], expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to start with {0}{reason}, but found {1}.", expected, items.Length > 0 ? items[0] : default);
        return new(this);
    }

    /// <summary>Asserts the collection starts with the given sequence of items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> StartWith(IEnumerable<T> expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var items = Items;
        var expectedItems = Spans.ListFrom(expected);
        if (Subject is null) return FailNull($"to start with {Formatting.Formatter.Format(expectedItems)}", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var matches = items.Length >= expectedItems.Count;
        for (var i = 0; matches && i < expectedItems.Count; i++)
        {
            matches = comparer.Equals(items[i], expectedItems[i]);
        }

        Assert().ForCondition(matches).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to start with {0}{reason}, but found {1}.", expectedItems, Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection's first item is not <paramref name="unexpected"/>, mirroring
    /// <c>string.Should().NotStartWith(...)</c>.
    /// </summary>
    /// <remarks>
    /// An empty collection starts with nothing at all, so it satisfies this. That is why the
    /// positive <see cref="StartWith(T, string?, object?[])"/> needs a separate "the collection is
    /// empty" failure and this one does not.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> NotStartWith(T unexpected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"not to start with {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);
        var items = Items;

        var startsWith = items.Length > 0 && EqualityComparer<T>.Default.Equals(items[0], unexpected);

        Assert().ForCondition(!startsWith).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to start with {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection does not begin with the given sequence of items. A collection shorter
    /// than <paramref name="unexpected"/> cannot begin with it, so it satisfies this.
    /// </summary>
    /// <remarks>
    /// An empty <paramref name="unexpected"/> sequence prefixes every collection, so this assertion
    /// always fails for one.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> NotStartWith(IEnumerable<T> unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var items = Items;
        var unexpectedItems = Spans.ListFrom(unexpected);
        if (Subject is null) return FailNull($"not to start with {Formatting.Formatter.Format(unexpectedItems)}", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var startsWith = items.Length >= unexpectedItems.Count;
        for (var i = 0; startsWith && i < unexpectedItems.Count; i++)
        {
            startsWith = comparer.Equals(items[i], unexpectedItems[i]);
        }

        Assert().ForCondition(!startsWith).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to start with {0}{reason}.", unexpectedItems);
        return new(this);
    }

    /// <summary>Asserts the collection ends with the given item.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> EndWith(T expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to end with {Formatting.Formatter.Format(expected)}", because, becauseArgs);
        var items = Items;

        Assert().ForCondition(items.Length > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to end with {0}{reason}, but the collection is empty.", expected)
            .ForCondition(items.Length == 0 || EqualityComparer<T>.Default.Equals(items[^1], expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to end with {0}{reason}, but found {1}.", expected, items.Length > 0 ? items[^1] : default);
        return new(this);
    }

    /// <summary>Asserts the collection ends with the given sequence of items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> EndWith(IEnumerable<T> expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var items = Items;
        var expectedItems = Spans.ListFrom(expected);
        if (Subject is null) return FailNull($"to end with {Formatting.Formatter.Format(expectedItems)}", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var matches = items.Length >= expectedItems.Count;
        var offset = items.Length - expectedItems.Count;
        for (var i = 0; matches && i < expectedItems.Count; i++)
        {
            matches = comparer.Equals(items[offset + i], expectedItems[i]);
        }

        Assert().ForCondition(matches).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to end with {0}{reason}, but found {1}.", expectedItems, Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection's last item is not <paramref name="unexpected"/>, mirroring
    /// <c>string.Should().NotEndWith(...)</c>. An empty collection ends with nothing at all, so it
    /// satisfies this.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotEndWith(T unexpected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"not to end with {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);
        var items = Items;

        var endsWith = items.Length > 0 && EqualityComparer<T>.Default.Equals(items[^1], unexpected);

        Assert().ForCondition(!endsWith).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to end with {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>
    /// Asserts the collection does not finish with the given sequence of items. A collection shorter
    /// than <paramref name="unexpected"/> cannot finish with it, so it satisfies this; an empty
    /// <paramref name="unexpected"/> sequence suffixes every collection, so this always fails for one.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotEndWith(IEnumerable<T> unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var items = Items;
        var unexpectedItems = Spans.ListFrom(unexpected);
        if (Subject is null) return FailNull($"not to end with {Formatting.Formatter.Format(unexpectedItems)}", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var endsWith = items.Length >= unexpectedItems.Count;
        var offset = items.Length - unexpectedItems.Count;
        for (var i = 0; endsWith && i < unexpectedItems.Count; i++)
        {
            endsWith = comparer.Equals(items[offset + i], unexpectedItems[i]);
        }

        Assert().ForCondition(!endsWith).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to end with {0}{reason}.", unexpectedItems);
        return new(this);
    }

    /// <summary>Asserts the items are in ascending order using <see cref="Comparer{T}.Default"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeInAscendingOrder(string? because = null, params object?[] becauseArgs)
        => BeInAscendingOrder(Comparer<T>.Default, because, becauseArgs);

    /// <summary>Asserts the items are in ascending order using the given comparer.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeInAscendingOrder(IComparer<T> comparer, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        return AssertOrder(comparer, descending: false, because, becauseArgs);
    }

    /// <summary>Asserts the items are in ascending order by the given key.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeInAscendingOrder<TKey>(Func<T, TKey> selector, string? because = null, params object?[] becauseArgs)
        where TKey : IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(selector);
        return AssertOrder(new KeyComparer<TKey>(selector), descending: false, because, becauseArgs);
    }

    /// <summary>Asserts the items are in descending order using <see cref="Comparer{T}.Default"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeInDescendingOrder(string? because = null, params object?[] becauseArgs)
        => BeInDescendingOrder(Comparer<T>.Default, because, becauseArgs);

    /// <summary>Asserts the items are in descending order using the given comparer.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeInDescendingOrder(IComparer<T> comparer, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        return AssertOrder(comparer, descending: true, because, becauseArgs);
    }

    /// <summary>Asserts the items are in descending order by the given key.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeInDescendingOrder<TKey>(Func<T, TKey> selector, string? because = null, params object?[] becauseArgs)
        where TKey : IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(selector);
        return AssertOrder(new KeyComparer<TKey>(selector), descending: true, because, becauseArgs);
    }

    /// <summary>
    /// Asserts the items are <em>not</em> in ascending order using <see cref="Comparer{T}.Default"/>
    /// — that is, some item is followed by a strictly smaller one.
    /// </summary>
    /// <remarks>
    /// Being ordered is a claim about every adjacent pair, so it holds vacuously for an empty or
    /// single-item collection and this negation fails for both. Note also that a collection of all
    /// equal items is <em>both</em> ascending and descending, so it fails this and
    /// <see cref="NotBeInDescendingOrder(string?, object?[])"/> alike.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeInAscendingOrder(string? because = null, params object?[] becauseArgs)
        => NotBeInAscendingOrder(Comparer<T>.Default, because, becauseArgs);

    /// <summary>Asserts the items are not in ascending order according to the given comparer.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeInAscendingOrder(IComparer<T> comparer, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        return AssertNotOrder(comparer, descending: false, because, becauseArgs);
    }

    /// <summary>Asserts the items are not in ascending order by the given key.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeInAscendingOrder<TKey>(Func<T, TKey> selector, string? because = null, params object?[] becauseArgs)
        where TKey : IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(selector);
        return AssertNotOrder(new KeyComparer<TKey>(selector), descending: false, because, becauseArgs);
    }

    /// <summary>
    /// Asserts the items are <em>not</em> in descending order using
    /// <see cref="Comparer{T}.Default"/> — that is, some item is followed by a strictly larger one.
    /// Fails for an empty or single-item collection, which is vacuously ordered.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeInDescendingOrder(string? because = null, params object?[] becauseArgs)
        => NotBeInDescendingOrder(Comparer<T>.Default, because, becauseArgs);

    /// <summary>Asserts the items are not in descending order according to the given comparer.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeInDescendingOrder(IComparer<T> comparer, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        return AssertNotOrder(comparer, descending: true, because, becauseArgs);
    }

    /// <summary>Asserts the items are not in descending order by the given key.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeInDescendingOrder<TKey>(Func<T, TKey> selector, string? because = null, params object?[] becauseArgs)
        where TKey : IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(selector);
        return AssertNotOrder(new KeyComparer<TKey>(selector), descending: true, because, becauseArgs);
    }

    private AndConstraint<GenericCollectionAssertions<T>> AssertNotOrder(IComparer<T> comparer, bool descending, string? because, object?[] becauseArgs)
    {
        var direction = descending ? "descending" : "ascending";
        if (Subject is null) return FailNull($"not to be in {direction} order", because, becauseArgs);
        var items = Items;

        var ordered = true;
        for (var i = 1; ordered && i < items.Length; i++)
        {
            var comparison = comparer.Compare(items[i - 1], items[i]);
            if (descending ? comparison < 0 : comparison > 0) ordered = false;
        }

        Assert().ForCondition(!ordered).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be in " + direction + " order{reason}, but found {0}.", Subject);
        return new(this);
    }

    private AndConstraint<GenericCollectionAssertions<T>> AssertOrder(IComparer<T> comparer, bool descending, string? because, object?[] becauseArgs)
    {
        var direction = descending ? "descending" : "ascending";
        if (Subject is null) return FailNull($"to be in {direction} order", because, becauseArgs);
        var items = Items;

        for (var i = 1; i < items.Length; i++)
        {
            var comparison = comparer.Compare(items[i - 1], items[i]);
            if (descending ? comparison < 0 : comparison > 0)
            {
                Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                    .FailWith("Expected {subject} to be in " + direction + " order{reason}, but found {0} before {1} at index {2}.",
                        items[i - 1], items[i], i - 1);
                return new(this);
            }
        }
        return new(this);
    }

    private sealed class KeyComparer<TKey> : IComparer<T> where TKey : IComparable<TKey>
    {
        private readonly Func<T, TKey> selector;
        public KeyComparer(Func<T, TKey> selector) => this.selector = selector;

        public int Compare(T? x, T? y)
        {
            var keyX = selector(x!);
            var keyY = selector(y!);
            if (keyX is null) return keyY is null ? 0 : -1;
            if (keyY is null) return 1;
            return keyX.CompareTo(keyY);
        }
    }

    /// <summary>Asserts every item of the collection is also part of <paramref name="expectedSuperset"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeSubsetOf(IEnumerable<T> expectedSuperset, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expectedSuperset);
        var items = Items;
        var superset = new HashSet<T>(expectedSuperset);
        if (Subject is null) return FailNull("to be a subset of the given superset", because, becauseArgs);

        var missing = new HashSet<T>();
        var missingInOrder = new List<T>();
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!superset.Contains(item) && missing.Add(item))
                missingInOrder.Add(item);
        }

        Assert().ForCondition(missingInOrder.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be a subset of {0}{reason}, but item(s) {1} are not part of the superset.", superset, missingInOrder);
        return new(this);
    }

    /// <summary>Asserts at least one item of the collection is not part of <paramref name="unexpectedSuperset"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeSubsetOf(IEnumerable<T> unexpectedSuperset, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpectedSuperset);
        var items = Items;
        var superset = new HashSet<T>(unexpectedSuperset);
        if (Subject is null) return FailNull("not to be a subset of the given superset", because, becauseArgs);

        var isSubset = true;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!superset.Contains(item)) { isSubset = false; break; }
        }

        Assert().ForCondition(!isSubset).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be a subset of {0}{reason}.", superset);
        return new(this);
    }

    /// <summary>Asserts the collection shares at least one item with <paramref name="otherCollection"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> IntersectWith(IEnumerable<T> otherCollection, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(otherCollection);
        var items = Items;
        var other = new HashSet<T>(otherCollection);
        if (Subject is null) return FailNull("to intersect with the other collection", because, becauseArgs);

        var intersects = false;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (other.Contains(item)) { intersects = true; break; }
        }

        Assert().ForCondition(intersects).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to intersect with {0}{reason}, but the collections do not share any items.", other);
        return new(this);
    }

    /// <summary>Asserts the collection shares no items with <paramref name="otherCollection"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotIntersectWith(IEnumerable<T> otherCollection, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(otherCollection);
        var items = Items;
        var other = new HashSet<T>(otherCollection);
        if (Subject is null) return FailNull("not to intersect with the other collection", because, becauseArgs);

        var shared = new HashSet<T>();
        var sharedInOrder = new List<T>();
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (other.Contains(item) && shared.Add(item))
                sharedInOrder.Add(item);
        }

        Assert().ForCondition(sharedInOrder.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to intersect with {0}{reason}, but found shared item(s) {1}.", other, sharedInOrder);
        return new(this);
    }

    /// <summary>
    /// Asserts every item satisfies the given assertion action. All failing items are aggregated
    /// (with their indexes) into a single failure.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> AllSatisfy(Action<T> assertion, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        if (Subject is null) return FailNull("to all satisfy the given assertion", because, becauseArgs);
        var items = Items;

        var failures = InspectItems(items, _ => assertion);
        Assert().ForCondition(failures.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to all satisfy the given assertion{reason}, but some items did not:"
                + Environment.NewLine + string.Join(Environment.NewLine, failures));
        return new(this);
    }

    /// <summary>
    /// Asserts the collection has exactly one item per inspector and each item satisfies its
    /// respective inspector. All failing items are aggregated (with their indexes) into a
    /// single failure.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> SatisfyRespectively(params Action<T>[] assertions)
        => SatisfyRespectively((IEnumerable<Action<T>>)assertions, null);

    /// <summary>
    /// Asserts the collection has exactly one item per inspector and each item satisfies its
    /// respective inspector. All failing items are aggregated (with their indexes) into a
    /// single failure.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> SatisfyRespectively(IEnumerable<Action<T>> assertions, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        var inspectors = Spans.ListFrom(assertions);
        if (inspectors.Count == 0) throw new ArgumentException("At least one inspector is required.", nameof(assertions));
        if (Subject is null) return FailNull($"to satisfy all {inspectors.Count} inspector(s)", because, becauseArgs);
        var items = Items;

        if (items.Length != inspectors.Count)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to satisfy all {0} inspector(s){reason}, but it contains {1} item(s): {2}.",
                    inspectors.Count, items.Length, Subject);
            return new(this);
        }

        var failures = InspectItems(items, i => inspectors[i]);
        Assert().ForCondition(failures.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to satisfy the respective inspectors{reason}, but some items did not:"
                + Environment.NewLine + string.Join(Environment.NewLine, failures));
        return new(this);
    }

    /// <summary>
    /// Runs one inspector per item, isolating each in its own <see cref="AssertionScope"/> so
    /// every failing item is captured (with its index) rather than only the first one. When an
    /// outer scope is active, the per-item scopes bubble into it instead and the returned list
    /// stays empty — the outer scope already carries the indexed failures.
    /// </summary>
    private static List<string> InspectItems(ReadOnlySpan<T> items, Func<int, Action<T>> inspectorFor)
    {
        var failures = new List<string>();
        for (var i = 0; i < items.Length; i++)
        {
            try
            {
                using var itemScope = new AssertionScope($"item at index {i}");
                inspectorFor(i)(items[i]);
            }
            catch (AssertionFailedException ex)
            {
                failures.Add(ex.Message);
            }
        }
        return failures;
    }

    /// <summary>Asserts every item is exactly of type <typeparamref name="TExpected"/> (not a derived type).</summary>
    public AndConstraint<GenericCollectionAssertions<T>> AllBeOfType<TExpected>(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to all be of type {typeof(TExpected).FullName}", because, becauseArgs);
        var items = Items;

        for (var i = 0; i < items.Length; i++)
        {
            if (items[i]?.GetType() != typeof(TExpected))
            {
                Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                    .FailWith("Expected {subject} to all be of type {0}{reason}, but the item at index {1} is {2}.",
                        typeof(TExpected), i, items[i]?.GetType());
                return new(this);
            }
        }
        return new(this);
    }

    /// <summary>Asserts every item is assignable to <typeparamref name="TExpected"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> AllBeAssignableTo<TExpected>(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to all be assignable to {typeof(TExpected).FullName}", because, becauseArgs);
        var items = Items;

        for (var i = 0; i < items.Length; i++)
        {
            if (items[i] is not TExpected)
            {
                Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                    .FailWith("Expected {subject} to all be assignable to {0}{reason}, but the item at index {1} is {2}.",
                        typeof(TExpected), i, items[i]?.GetType());
                return new(this);
            }
        }
        return new(this);
    }

    /// <summary>
    /// Asserts at least one item is <em>not</em> exactly of type <typeparamref name="TExpected"/> —
    /// a derived type or a <see langword="null"/> item counts as "not", exactly as it does for
    /// <see cref="AllBeOfType{TExpected}"/>.
    /// </summary>
    /// <remarks>
    /// This is the logical negation of a universally quantified claim, so it fails for an empty
    /// collection, which vacuously has all its items of any type you like. It does not mean "no item
    /// is of this type"; a mixed collection satisfies it.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> NotAllBeOfType<TExpected>(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"not to all be of type {typeof(TExpected).FullName}", because, becauseArgs);
        var items = Items;

        var allMatch = true;
        for (var i = 0; allMatch && i < items.Length; i++)
        {
            if (items[i]?.GetType() != typeof(TExpected)) allMatch = false;
        }

        Assert().ForCondition(!allMatch).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to all be of type {0}{reason}, but all {1} item(s) are.", typeof(TExpected), items.Length);
        return new(this);
    }

    /// <summary>
    /// Asserts at least one item is <em>not</em> assignable to <typeparamref name="TExpected"/>.
    /// Fails for an empty collection, which is vacuously all-assignable to anything.
    /// </summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotAllBeAssignableTo<TExpected>(string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"not to all be assignable to {typeof(TExpected).FullName}", because, becauseArgs);
        var items = Items;

        var allMatch = true;
        for (var i = 0; allMatch && i < items.Length; i++)
        {
            if (items[i] is not TExpected) allMatch = false;
        }

        Assert().ForCondition(!allMatch).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to all be assignable to {0}{reason}, but all {1} item(s) are.", typeof(TExpected), items.Length);
        return new(this);
    }

    private static int Count(System.Collections.IEnumerable enumerable)
    {
        if (enumerable is System.Collections.ICollection collection) return collection.Count;

        var count = 0;
        var enumerator = enumerable.GetEnumerator();
        try
        {
            while (enumerator.MoveNext()) count++;
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
        return count;
    }

    /// <summary>Asserts the collection contains every one of <paramref name="expected"/>.</summary>
    /// <remarks>
    /// Named <c>ContainAll</c>, not an overload of <c>Contain</c>, following the convention
    /// <c>StringAssertions</c> already sets (<c>ContainAll</c> / <c>NotContainAny</c> /
    /// <c>NotContainAll</c>).
    ///
    /// FluentAssertions spells this as another <c>Contain</c> overload, and that shape has a trap:
    /// <c>Contain(oneItem)</c> matches both the single-item overload and a <c>params</c> bulk one in
    /// EXPANDED form only — neither is applicable in normal form, because both end in a params
    /// parameter with no argument — and the tie goes to the candidate needing no defaulted
    /// arguments, i.e. the bulk one. It compiles and reports a collection-shaped message for what
    /// the author wrote as a single-item assertion. A distinct name removes the ambiguity outright,
    /// and lets the terse <c>params</c> form exist safely.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> ContainAll(params T[] expected)
        => ContainAll((IEnumerable<T>)expected, because: null);

    /// <summary>Asserts the collection contains every one of <paramref name="expected"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> ContainAll(IEnumerable<T> expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var actual = Items;
        if (Subject is null) return FailNull("to contain the expected items", because, becauseArgs);

        var missing = new List<T>();
        foreach (var item in expected)
        {
            if (!Includes(actual, item)) missing.Add(item);
        }

        Assert().ForCondition(missing.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0}{reason}, but could not find {1}.", expected, missing);
        return new(this);
    }

    /// <summary>Asserts the collection contains none of <paramref name="unexpected"/>.</summary>
    /// <remarks>
    /// <c>NotContainAny</c> rather than <c>NotContain</c>: it asserts that NONE of the values are
    /// present, which is what <c>NotContainAny</c> says and what <c>NotContain</c> leaves open.
    /// Matches <c>StringAssertions.NotContainAny</c>. See <c>ContainAll</c> for why bulk membership
    /// does not share the single-item name.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> NotContainAny(params T[] unexpected)
        => NotContainAny((IEnumerable<T>)unexpected, because: null);

    /// <summary>Asserts the collection contains none of <paramref name="unexpected"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContainAny(IEnumerable<T> unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var actual = Items;
        if (Subject is null) return FailNull("not to contain the given items", because, becauseArgs);

        var found = new List<T>();
        foreach (var item in unexpected)
        {
            if (Includes(actual, item)) found.Add(item);
        }

        Assert().ForCondition(found.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0}{reason}, but found {1}.", unexpected, found);
        return new(this);
    }

    /// <summary>Asserts every item of <paramref name="expectedSubset"/> is present in the collection.</summary>
    /// <remarks>The mirror of <c>BeSubsetOf</c>, from the other side of the relation.</remarks>
    public AndConstraint<GenericCollectionAssertions<T>> BeSupersetOf(IEnumerable<T> expectedSubset, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expectedSubset);
        var actual = Items;
        if (Subject is null) return FailNull("to be a superset of the given collection", because, becauseArgs);

        var missing = new List<T>();
        foreach (var item in expectedSubset)
        {
            if (!Includes(actual, item)) missing.Add(item);
        }

        Assert().ForCondition(missing.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be a superset of {0}{reason}, but it is missing {1}.", expectedSubset, missing);
        return new(this);
    }

    /// <summary>Asserts the collection is not a superset of <paramref name="unexpectedSubset"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeSupersetOf(IEnumerable<T> unexpectedSubset, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpectedSubset);
        var actual = Items;
        if (Subject is null) return FailNull("not to be a superset of the given collection", because, becauseArgs);

        var containsAll = true;
        foreach (var item in unexpectedSubset)
        {
            if (!Includes(actual, item)) { containsAll = false; break; }
        }

        Assert().ForCondition(!containsAll).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be a superset of {0}{reason}.", unexpectedSubset);
        return new(this);
    }

    /// <summary>Asserts the item at <paramref name="index"/> equals <paramref name="expected"/>, and exposes it via <c>Which</c>.</summary>
    public AndWhichConstraint<GenericCollectionAssertions<T>, T> HaveElementAt(int index, T expected, string? because = null, params object?[] becauseArgs)
    {
        var actual = Items;
        if (Subject is null)
        {
            FailNull("to have an element at the given index", because, becauseArgs);
            return new(this, default!);
        }

        Assert().ForCondition(index >= 0 && index < actual.Length).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have an element at index {0}{reason}, but it has only {1} item(s).", index, actual.Length);
        if (index < 0 || index >= actual.Length) return new(this, default!);

        var found = actual[index];
        Assert().ForCondition(EqualityComparer<T>.Default.Equals(found, expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have {0} at index {1}{reason}, but found {2}.", expected, index, found);
        return new(this, found);
    }

    /// <summary>Asserts <paramref name="successor"/> appears in the collection immediately after <paramref name="expected"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveElementPreceding(T successor, T expected, string? because = null, params object?[] becauseArgs)
    {
        var actual = Items;
        if (Subject is null) return FailNull("to have an element preceding the given item", because, becauseArgs);

        var index = IndexOf(actual, successor);
        Assert().ForCondition(index > 0 && EqualityComparer<T>.Default.Equals(actual[index - 1], expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have {0} preceding {1}{reason}.", expected, successor);
        return new(this);
    }

    /// <summary>Asserts <paramref name="predecessor"/> appears in the collection immediately before <paramref name="expected"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveElementSucceeding(T predecessor, T expected, string? because = null, params object?[] becauseArgs)
    {
        var actual = Items;
        if (Subject is null) return FailNull("to have an element succeeding the given item", because, becauseArgs);

        var index = IndexOf(actual, predecessor);
        Assert().ForCondition(index >= 0 && index + 1 < actual.Length && EqualityComparer<T>.Default.Equals(actual[index + 1], expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have {0} succeeding {1}{reason}.", expected, predecessor);
        return new(this);
    }

    /// <summary>Asserts every item is assignable to <typeparamref name="TExpected"/>, ignoring nulls.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> ContainItemsAssignableTo<TExpected>(string? because = null, params object?[] becauseArgs)
    {
        var actual = Items;
        if (Subject is null) return FailNull("to contain items assignable to the given type", because, becauseArgs);

        var offender = -1;
        for (var i = 0; i < actual.Length; i++)
        {
            if (actual[i] is not TExpected) { offender = i; break; }
        }

        Assert().ForCondition(offender < 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain only items assignable to {0}{reason}, but item at index {1} is {2}.",
                typeof(TExpected), offender, offender < 0 ? null : actual[offender]?.GetType());
        return new(this);
    }

    /// <summary>Asserts the items are unique by the key produced by <paramref name="keySelector"/>.</summary>
    /// <remarks>
    /// Uniqueness by a projection is what real tests usually want — unique Ids, unique names — where
    /// the items themselves compare by reference and are trivially all distinct.
    /// </remarks>
    public AndConstraint<GenericCollectionAssertions<T>> OnlyHaveUniqueItems<TKey>(Func<T, TKey> keySelector, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        var actual = Items;
        if (Subject is null) return FailNull("to only have unique items", because, becauseArgs);

        var seen = new HashSet<TKey>();
        var duplicates = new List<TKey>();
        for (var i = 0; i < actual.Length; i++)
        {
            var item = actual[i];
            var key = keySelector(item);
            if (!seen.Add(key)) duplicates.Add(key);
        }

        Assert().ForCondition(duplicates.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to only have unique items{reason}, but found duplicate key(s) {0}.", duplicates);
        return new(this);
    }

    /// <summary>Asserts no item has a null key as produced by <paramref name="keySelector"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContainNulls<TKey>(Func<T, TKey> keySelector, string? because = null, params object?[] becauseArgs)
        where TKey : class
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        var actual = Items;
        if (Subject is null) return FailNull("not to contain items with a null key", because, becauseArgs);

        var offender = -1;
        for (var i = 0; i < actual.Length; i++)
        {
            if (keySelector(actual[i]) is null) { offender = i; break; }
        }

        Assert().ForCondition(offender < 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain items with a null key{reason}, but found one at index {0}.", offender);
        return new(this);
    }

    // Membership helpers: explicit INDEXED loops, not LINQ and not foreach. LINQ Contains/IndexOf
    // would allocate an enumerator and a closure; foreach over the IReadOnlyList<T> interface boxes
    // the underlying struct enumerator. Indexing allocates nothing.
    private static bool Includes(ReadOnlySpan<T> source, T value)
    {
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < source.Length; i++)
        {
            if (comparer.Equals(source[i], value)) return true;
        }

        return false;
    }

    private static int IndexOf(ReadOnlySpan<T> source, T value)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (EqualityComparer<T>.Default.Equals(source[i], value)) return i;
        }

        return -1;
    }

    /// <summary>Collection size at or below which duplicate detection scans pairwise instead of building a set.</summary>
    /// <remarks>
    /// 32 items is 496 comparisons worst case, which is far cheaper than the allocation and hashing
    /// a <see cref="HashSet{T}"/> would cost — and it allocates nothing, which is the point.
    /// </remarks>
    private const int PairwiseScanThreshold = 32;

    private static bool HasDuplicates(ReadOnlySpan<T> items)
    {
        if (items.Length > PairwiseScanThreshold)
        {
            var set = new HashSet<T>();
            for (var i = 0; i < items.Length; i++)
            {
                if (!set.Add(items[i])) return true;
            }

            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < items.Length; i++)
        {
            for (var j = i + 1; j < items.Length; j++)
            {
                if (comparer.Equals(items[i], items[j])) return true;
            }
        }

        return false;
    }
}
