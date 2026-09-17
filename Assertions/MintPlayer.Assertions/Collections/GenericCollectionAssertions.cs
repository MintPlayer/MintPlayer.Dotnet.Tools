using System.Runtime.InteropServices;
using MintPlayer.Assertions.Execution;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Collections;

/// <summary>
/// Assertions on any <see cref="IEnumerable{T}"/>: emptiness, counts, membership, ordering,
/// set relations and per-item inspection. The subject is materialized at most once per
/// assertions instance, so lazily-evaluated sequences are never enumerated multiple times.
/// </summary>
public partial class GenericCollectionAssertions<T> : ReferenceTypeAssertions<IEnumerable<T>, GenericCollectionAssertions<T>>
{
    /// <summary>
    /// Whether an item of type <typeparamref name="T"/> can be <see langword="null"/> at all.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This is a field, not an inline <c>default(T) is null</c>, and that is the whole point.</b>
    /// Every null test against an unconstrained generic emits <c>box !T</c> — including the guard
    /// itself. Writing the guard inline turned <c>NotContainNulls</c> over an <c>int[8]</c> from
    /// 192 B/op (one box per item) into 24 B/op (one box for the guard), which is better and still
    /// not zero. As a static readonly field the box happens once per closed generic type, at type
    /// initialisation, and the per-call cost is a static bool read the JIT folds away.
    /// Measured by <c>PassingPathAllocationTests.IteratingTheSubjectAllocatesNothing</c>.
    /// </remarks>
    private static readonly bool ItemsCanBeNull = default(T) is null;

    private T[]? copy;
    private bool materialized;

    public GenericCollectionAssertions(IEnumerable<T>? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>The subject as a span, copied only when it is not already contiguous.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A span, not <c>IReadOnlyList&lt;T&gt;</c>, and the two type tests are ordered deliberately.</b>
    /// This was <c>items = Subject is null ? null : [.. Subject]</c>, which did two expensive things on
    /// every assertion: it <b>copied a subject that was already an array or a <c>List&lt;T&gt;</c></b>,
    /// and its interface type made every <c>foreach</c> over it box a struct enumerator — 16 loops in
    /// this file, which MPA0005 now reports.
    /// </para>
    /// <para>
    /// The array case is tested <b>before</b> the <c>List&lt;T&gt;</c> case, and that ordering is
    /// load-bearing: an array is not a <c>List&lt;T&gt;</c>, so testing only for the list silently drops
    /// every <c>T[]</c> subject onto the copying path — and arrays are what test code writes.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="CollectionsMarshal.AsSpan"/> returns the list.s own backing array, so the span is
    /// invalidated if the list is resized. That is safe only because no assertion mutates the sequence
    /// it was handed. Do not add one that does.
    /// </para>
    /// <para>
    /// A span cannot represent null, so the null checks moved to <c>Subject is null</c> — where the
    /// question belonged anyway. A lazy sequence is still materialised exactly once and cached:
    /// re-enumerating a one-shot iterator per assertion would be a correctness bug, not a slow path.
    /// </para>
    /// </remarks>
    private ReadOnlySpan<T> Items
    {
        get
        {
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
        var items = Items;
        if (Subject is null) return FailNull("to be empty", because, becauseArgs);

        Assert().ForCondition(items.Length == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be empty{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains at least one item.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotBeEmpty(string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull("not to be empty", because, becauseArgs);

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
            .FailWith("Expected {subject} not to be null or empty{reason}, but found {0}.", (object?)Subject);
        return new(this);
    }

    /// <summary>Asserts the collection is either null or empty.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> BeNullOrEmpty(string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        Assert().ForCondition(Subject is null || items.Length == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be null or empty{reason}, but found {0}.", (object?)Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains exactly <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCount(int expected, string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull($"to contain {expected} item(s)", because, becauseArgs);

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
        var items = Items;
        if (Subject is null) return FailNull($"not to contain {unexpected} item(s)", because, becauseArgs);

        Assert().ForCondition(items.Length != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0} item(s){reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection's count matches the given predicate.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCount(Func<int, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var items = Items;
        if (Subject is null) return FailNull("to have a count matching the given predicate", because, becauseArgs);

        Assert().ForCondition(predicate(items.Length)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a count matching the given predicate{reason}, but count is {0}: {1}.", items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains more than <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCountGreaterThan(int expected, string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull($"to contain more than {expected} item(s)", because, becauseArgs);

        Assert().ForCondition(items.Length > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain more than {0} item(s){reason}, but found {1}: {2}.", expected, items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains at least <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCountGreaterThanOrEqualTo(int expected, string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull($"to contain at least {expected} item(s)", because, becauseArgs);

        Assert().ForCondition(items.Length >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain at least {0} item(s){reason}, but found {1}: {2}.", expected, items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains fewer than <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCountLessThan(int expected, string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull($"to contain fewer than {expected} item(s)", because, becauseArgs);

        Assert().ForCondition(items.Length < expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain fewer than {0} item(s){reason}, but found {1}: {2}.", expected, items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains at most <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveCountLessThanOrEqualTo(int expected, string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull($"to contain at most {expected} item(s)", because, becauseArgs);

        Assert().ForCondition(items.Length <= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain at most {0} item(s){reason}, but found {1}: {2}.", expected, items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection has the same number of items as <paramref name="otherCollection"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> HaveSameCountAs(System.Collections.IEnumerable otherCollection, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(otherCollection);
        var items = Items;
        if (Subject is null) return FailNull("to have the same count as the other collection", because, becauseArgs);

        var expectedCount = Count(otherCollection);
        Assert().ForCondition(items.Length == expectedCount).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have {0} item(s), the same count as the other collection{reason}, but found {1}: {2}.", expectedCount, items.Length, Subject);
        return new(this);
    }

    /// <summary>Asserts the collection does not have the same number of items as <paramref name="otherCollection"/>.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotHaveSameCountAs(System.Collections.IEnumerable otherCollection, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(otherCollection);
        var items = Items;
        if (Subject is null) return FailNull("not to have the same count as the other collection", because, becauseArgs);

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
        var items = Items;
        if (Subject is null)
        {
            FailNull("to contain a single item", because, becauseArgs);
            return new(this, default!);
        }

        Assert().ForCondition(items.Length == 1).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain a single item{reason}, but found {0}: {1}.", items.Length, Subject);
        return new(this, items.Length == 1 ? items[0] : default!);
    }

    /// <summary>Asserts exactly one item matches the predicate, and exposes it via Which.</summary>
    public AndWhichConstraint<GenericCollectionAssertions<T>, T> ContainSingle(Func<T, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var items = Items;
        if (Subject is null)
        {
            FailNull("to contain a single item matching the given predicate", because, becauseArgs);
            return new(this, default!);
        }

        // Counted rather than collected. The sibling assertions make their failure-detail list lazy
        // and test `is null`, but that does not work here: this one PASSES on exactly one match, so
        // a lazy list would be allocated on the successful path every time. Tracking the count and
        // the first match keeps the passing path allocation-free, and the list is built only in the
        // branch that is already about to fail.
        var matchCount = 0;
        var first = default(T);
        for (var i = 0; i < items.Length; i++)
        {
            if (!predicate(items[i])) continue;
            if (matchCount == 0) first = items[i];
            matchCount++;
        }

        if (matchCount != 1)
        {
            var matches = new List<T>();
            for (var i = 0; i < items.Length; i++)
            {
                if (predicate(items[i])) matches.Add(items[i]);
            }

            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to contain a single item matching the given predicate{reason}, but found {0}: {1}.", matchCount, matches);
        }

        return new(this, matchCount >= 1 ? first! : default!);
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
        var items = Items;
        if (Subject is null) return FailNull("not to contain a single item", because, becauseArgs);

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
        var items = Items;
        if (Subject is null) return FailNull("not to contain a single item matching the given predicate", because, becauseArgs);

        // Counted, not collected — and for a subtler reason than its positive counterpart. This
        // assertion passes when the count is anything other than one, INCLUDING two or more. A lazy
        // list would therefore still be allocated, and populated, on a perfectly successful call
        // over a collection where several items match. Only the count decides the outcome, so only
        // the count is computed; the single matching item is collected in the failing branch.
        var matchCount = 0;
        for (var i = 0; i < items.Length; i++)
        {
            if (predicate(items[i])) matchCount++;
        }

        if (matchCount == 1)
        {
            var matches = new List<T>();
            for (var i = 0; i < items.Length; i++)
            {
                if (predicate(items[i])) matches.Add(items[i]);
            }

            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Did not expect {subject} to contain a single item matching the given predicate{reason}, but found {0}.", matches);
        }

        return new(this);
    }

    /// <summary>Asserts the collection contains the given item.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> Contain(T expected, string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull($"to contain {Formatting.Formatter.Format(expected)}", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var found = false;
        foreach (var item in items)
        {
            if (comparer.Equals(item, expected)) { found = true; break; }
        }

        Assert().ForCondition(found).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts at least one item matches the predicate, and exposes the first match via Which.</summary>
    public AndWhichConstraint<GenericCollectionAssertions<T>, T> Contain(Func<T, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var items = Items;
        if (Subject is null)
        {
            FailNull("to contain an item matching the given predicate", because, becauseArgs);
            return new(this, default!);
        }

        var found = false;
        T match = default!;
        foreach (var item in items)
        {
            if (predicate(item)) { found = true; match = item; break; }
        }

        Assert().ForCondition(found).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain an item matching the given predicate{reason}, but found {0}.", Subject);
        return new(this, match);
    }

    /// <summary>Asserts the collection does not contain the given item.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContain(T unexpected, string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull($"not to contain {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var found = false;
        foreach (var item in items)
        {
            if (comparer.Equals(item, unexpected)) { found = true; break; }
        }

        Assert().ForCondition(!found).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts no item matches the predicate.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContain(Func<T, bool> predicate, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var items = Items;
        if (Subject is null) return FailNull("not to contain an item matching the given predicate", because, becauseArgs);

        List<T>? matches = null;  // allocated only when the assertion is going to fail
        foreach (var item in items)
        {
            if (predicate(item)) (matches ??= []).Add(item);
        }

        Assert().ForCondition(matches is null).BecauseOf(because, becauseArgs)
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
        var expectedItems = expected as IReadOnlyList<T> ?? [.. expected];
        if (Subject is null) return FailNull($"to contain {Formatting.Formatter.Format(expectedItems)} in order", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var position = 0;
        foreach (var item in items)
        {
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
        var unexpectedItems = unexpected as IReadOnlyList<T> ?? [.. unexpected];
        if (Subject is null) return FailNull($"not to contain {Formatting.Formatter.Format(unexpectedItems)} in order", because, becauseArgs);

        var comparer = EqualityComparer<T>.Default;
        var position = 0;
        foreach (var item in items)
        {
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
        var items = Items;
        if (Subject is null) return FailNull("to only contain items matching the given predicate", because, becauseArgs);

        List<T>? mismatches = null;  // allocated only when the assertion is going to fail
        foreach (var item in items)
        {
            if (!predicate(item)) (mismatches ??= []).Add(item);
        }

        Assert().ForCondition(mismatches is null).BecauseOf(because, becauseArgs)
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
        var items = Items;
        if (Subject is null) return FailNull("not to only contain items matching the given predicate", because, becauseArgs);

        var allMatch = true;
        foreach (var item in items)
        {
            if (!predicate(item)) { allMatch = false; break; }
        }

        Assert().ForCondition(!allMatch).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to only contain items matching the given predicate{reason}, but all {0} item(s) did.", items.Length);
        return new(this);
    }

    /// <summary>Asserts the collection contains no duplicate items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> OnlyHaveUniqueItems(string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull("to only have unique items", because, becauseArgs);

        var seen = new HashSet<T>();
        var duplicates = new HashSet<T>();
        List<T>? duplicatesInOrder = null;  // allocated only when the assertion is going to fail
        foreach (var item in items)
        {
            if (!seen.Add(item) && duplicates.Add(item))
                (duplicatesInOrder ??= []).Add(item);
        }

        Assert().ForCondition(duplicatesInOrder is null).BecauseOf(because, becauseArgs)
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
        var items = Items;
        if (Subject is null) return FailNull("not to only have unique items", because, becauseArgs);

        var seen = new HashSet<T>();
        var hasDuplicate = false;
        foreach (var item in items)
        {
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
        var items = Items;
        if (Subject is null) return FailNull("to contain <null> items", because, becauseArgs);

        // ⚠️ `ItemsCanBeNull` is a guard against BOXING, not a shortcut. On an unconstrained T
        // the compiler emits `box !T` before the null test, so `items[i] is null` over an int[]
        // allocates 24 B per item to answer a question whose answer is always "no". The JIT folds
        // this outer test to a constant per instantiation, so the whole loop disappears for a value
        // type and costs nothing for a reference type. See NotContainNulls for the measurement.
        var containsNull = false;
        if (ItemsCanBeNull)
        {
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i] is null) { containsNull = true; break; }
            }
        }

        Assert().ForCondition(containsNull).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain <null> items{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the collection contains no null items.</summary>
    public AndConstraint<GenericCollectionAssertions<T>> NotContainNulls(string? because = null, params object?[] becauseArgs)
    {
        var items = Items;
        if (Subject is null) return FailNull("not to contain <null> items", because, becauseArgs);

        // Allocated only once a null is actually found. It used to be created unconditionally and
        // thrown away empty, which cost 32 B/op on the passing path of every call — a collection
        // that exists purely to be rendered into a failure message, built by assertions that pass.
        // PassingPathAllocationTests caught this; nothing else would have.
        // ⚠️ The `ItemsCanBeNull` guard is load-bearing and must not be removed as redundant.
        // `items[i] is null` on an unconstrained T compiles to `box !T` followed by a null test, so
        // over an int[8] this loop allocated 192 B/op — 24 B per item — to discover eight times that
        // an int is not null. Measured by IteratingTheSubjectAllocatesNothing, which is the only
        // reason it was ever noticed: the source reads as a plain null check either way. The JIT
        // constant-folds the guard per instantiation, so a value type skips the loop entirely and a
        // reference type pays nothing for the test.
        List<int>? nullIndexes = null;
        if (ItemsCanBeNull)
        {
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i] is null) (nullIndexes ??= []).Add(i);
            }
        }

        Assert().ForCondition(nullIndexes is null).BecauseOf(because, becauseArgs)
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
        var expectedItems = expected as IReadOnlyList<T> ?? [.. expected];
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
        var unexpectedItems = unexpected as IReadOnlyList<T> ?? [.. unexpected];
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
        var items = Items;
        if (Subject is null) return FailNull($"to start with {Formatting.Formatter.Format(expected)}", because, becauseArgs);

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
        var expectedItems = expected as IReadOnlyList<T> ?? [.. expected];
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
        var items = Items;
        if (Subject is null) return FailNull($"not to start with {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);

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
        var unexpectedItems = unexpected as IReadOnlyList<T> ?? [.. unexpected];
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
        var items = Items;
        if (Subject is null) return FailNull($"to end with {Formatting.Formatter.Format(expected)}", because, becauseArgs);

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
        var expectedItems = expected as IReadOnlyList<T> ?? [.. expected];
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
        var items = Items;
        if (Subject is null) return FailNull($"not to end with {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);

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
        var unexpectedItems = unexpected as IReadOnlyList<T> ?? [.. unexpected];
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
        var items = Items;
        if (Subject is null) return FailNull($"not to be in {direction} order", because, becauseArgs);

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
        var items = Items;
        if (Subject is null) return FailNull($"to be in {direction} order", because, becauseArgs);

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
        List<T>? missingInOrder = null;  // allocated only when the assertion is going to fail
        foreach (var item in items)
        {
            if (!superset.Contains(item) && missing.Add(item))
                (missingInOrder ??= []).Add(item);
        }

        Assert().ForCondition(missingInOrder is null).BecauseOf(because, becauseArgs)
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
        foreach (var item in items)
        {
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
        foreach (var item in items)
        {
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
        List<T>? sharedInOrder = null;  // allocated only when the assertion is going to fail
        foreach (var item in items)
        {
            if (other.Contains(item) && shared.Add(item))
                (sharedInOrder ??= []).Add(item);
        }

        Assert().ForCondition(sharedInOrder is null).BecauseOf(because, becauseArgs)
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
        var items = Items;
        if (Subject is null) return FailNull("to all satisfy the given assertion", because, becauseArgs);

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
        var inspectors = assertions as IReadOnlyList<Action<T>> ?? [.. assertions];
        if (inspectors.Count == 0) throw new ArgumentException("At least one inspector is required.", nameof(assertions));
        var items = Items;
        if (Subject is null) return FailNull($"to satisfy all {inspectors.Count} inspector(s)", because, becauseArgs);

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
        // NOT made lazy, unlike the failure-detail lists elsewhere in this file, and the reason is
        // worth stating so nobody "finishes the job": this list is the RETURN VALUE, not a detail
        // collected for a message. Its allocation is the method's purpose.
        //
        // It would also be pointless. The loop below allocates an AssertionScope and an interpolated
        // string PER ITEM, and relies on exception-based control flow for each failing one — costs
        // that dwarf a single List by orders of magnitude. If this path is ever worth optimising,
        // the scope-per-item is the thing to attack, not this.
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
        var items = Items;
        if (Subject is null) return FailNull($"to all be of type {typeof(TExpected).FullName}", because, becauseArgs);

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
        var items = Items;
        if (Subject is null) return FailNull($"to all be assignable to {typeof(TExpected).FullName}", because, becauseArgs);

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
        var items = Items;
        if (Subject is null) return FailNull($"not to all be of type {typeof(TExpected).FullName}", because, becauseArgs);

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
        var items = Items;
        if (Subject is null) return FailNull($"not to all be assignable to {typeof(TExpected).FullName}", because, becauseArgs);

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
}
