using System.Runtime.InteropServices;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Collections;

/// <summary>
/// Assertions on dictionaries and any other sequence of key/value pairs: emptiness, counts and
/// key/value/pair membership. The subject is materialized at most once per assertions instance,
/// so lazily-evaluated sequences are never enumerated multiple times.
/// </summary>
/// <remarks>
/// ⚠️ <b>MPA0005 reports four remaining boxed-enumerator loops in this file, and they are deliberate.</b>
/// They are the <c>expected</c>/<c>unexpected</c> sequences — the EXPECTATION side — materialised as
/// <c>x as IReadOnlyList&lt;T&gt; ?? [.. x]</c> and iterated once per assertion over however many
/// items the caller happened to write. The subject side is a <see cref="ReadOnlySpan{T}"/> and
/// allocates nothing, which is the asymmetry that matters: the subject is walked by every assertion
/// in a suite; an expectation is a handful of items in one call.
/// <para>
/// Converting them is not simply low-value, it fights the type system. A <c>ReadOnlySpan&lt;T&gt;</c>
/// is a ref struct, so it cannot be passed to <c>FailWith</c>'s <c>object?</c> parameters — and
/// these sequences are rendered into failure messages — nor captured by a lambda, which the
/// inspector assertions do. An earlier attempt produced 52 compiler errors of exactly those two
/// kinds before being reverted. Suppress or leave; do not "finish the job" without re-reading this.
/// </para>
/// </remarks>
public class GenericDictionaryAssertions<TKey, TValue> : ReferenceTypeAssertions<IEnumerable<KeyValuePair<TKey, TValue>>, GenericDictionaryAssertions<TKey, TValue>>
{
    /// <summary>
    /// Whether a key of type <typeparamref name="TKey"/> can be <see langword="null"/> at all.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A field, not an inline <c>default(TKey) is null</c>.</b> A null test against an
    /// unconstrained generic emits <c>box !TKey</c>, and so does the guard, so writing it inline
    /// merely moves the allocation rather than removing it — measured as 24 B/op still on the passing
    /// path of every <c>ContainKey</c> over a <c>Dictionary&lt;int, …&gt;</c>. As a static readonly
    /// field the box happens once per closed generic type. Same reasoning as
    /// <c>GenericCollectionAssertions&lt;T&gt;.ItemsCanBeNull</c>; gated by
    /// <c>PassingPathAllocationTests.LookingUpAValueTypeKeyAllocatesNothing</c>.
    /// </remarks>
    private static readonly bool KeysCanBeNull = default(TKey) is null;

    private KeyValuePair<TKey, TValue>[]? copy;
    private bool materialized;

    public GenericDictionaryAssertions(IEnumerable<KeyValuePair<TKey, TValue>>? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>The subject as a span, copied only when it is not already contiguous.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ A span, not <c>IReadOnlyList&lt;&gt;</c>. The interface type made every <c>foreach</c> over
    /// this box a struct enumerator — 10 loops in this file, which MPA0005 now reports. See
    /// <c>GenericCollectionAssertions.Items</c> for the measurement and the array-before-List rule.
    /// </para>
    /// <para>
    /// Unlike the collection case, the common subject here — a <c>Dictionary&lt;TKey, TValue&gt;</c> — is
    /// neither an array nor a <c>List&lt;&gt;</c>, so it still copies. That is not a missed optimisation:
    /// a hash table is not contiguous, so there is no span to hand back without one. The win here is
    /// the boxing, not the copy.
    /// </para>
    /// <para>
    /// ⚠️ <b>Because it copies, reading this property is not free — do not open a method with
    /// <c>var pairs = Pairs;</c> out of habit.</b> Six key-based methods (<c>ContainKey</c>,
    /// <c>ContainKeys</c>, <c>NotContainKey</c>, <c>NotContainKeys</c>, <c>Contain</c>,
    /// <c>NotContain</c>) did exactly that and never read the local: they answer through
    /// <see cref="TryGetValueForKey"/>, which goes straight at the dictionary's own
    /// <c>TryGetValue</c>. The unused line cost a full copy of the subject on every call — 56 B/op
    /// for a two-entry <c>Dictionary&lt;int, string&gt;</c>, and proportional to the dictionary for a
    /// real one. It reads as harmless setup, and it is the most expensive line in the method.
    /// <c>LookingUpAValueTypeKeyAllocatesNothing</c> gates it now.
    /// </para>
    /// </remarks>
    private ReadOnlySpan<KeyValuePair<TKey, TValue>> Pairs
    {
        get
        {
            if (Subject is KeyValuePair<TKey, TValue>[] array) return array;
            if (Subject is List<KeyValuePair<TKey, TValue>> list) return CollectionsMarshal.AsSpan(list);

            if (!materialized)
            {
                copy = Subject is null ? null : [.. Subject];
                materialized = true;
            }
            return copy;
        }
    }

    /// <summary>
    /// Looks a key up the way the subject itself would. A dictionary carries its own
    /// <see cref="IEqualityComparer{T}"/> — <c>StringComparer.OrdinalIgnoreCase</c>, say — so
    /// comparing keys with <see cref="EqualityComparer{T}.Default"/> instead would report a key as
    /// missing that the dictionary really holds, and would let <see cref="NotContainKey"/> pass
    /// for a key that is present. Sequences of pairs that are not dictionaries have no comparer of
    /// their own and fall back to the default one.
    /// </summary>
    private bool TryGetValueForKey(TKey key, out TValue value)
    {
        // A null key throws in Dictionary<,>.TryGetValue, so scan for it instead.
        //
        // ⚠️ The `!KeysCanBeNull` half is there to avoid a BOX, not to save a branch.
        // `key is not null` on an unconstrained TKey emits `box !TKey`, so every ContainKey on a
        // Dictionary<int, …> allocated 24 B to ask whether an int is null. The guard is a cached static
        // bool, so a value-type key short-circuits before the box
        // and a reference-type key runs exactly the check it used to. Same trap as the item loops in
        // GenericCollectionAssertions.NotContainNulls, which is where it was first measured.
        if (!KeysCanBeNull || key is not null)
        {
            switch (Subject)
            {
                case IDictionary<TKey, TValue> dictionary:
                    return dictionary.TryGetValue(key, out value!);
                case IReadOnlyDictionary<TKey, TValue> readOnlyDictionary:
                    return readOnlyDictionary.TryGetValue(key, out value!);
            }
        }

        var comparer = EqualityComparer<TKey>.Default;
        foreach (var pair in Pairs)
        {
            if (comparer.Equals(pair.Key, key))
            {
                value = pair.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    private AndConstraint<GenericDictionaryAssertions<TKey, TValue>> FailNull(string expectation, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} " + expectation + "{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the dictionary contains no items.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> BeEmpty(string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (Subject is null) return FailNull("to be empty", because, becauseArgs);

        Assert().ForCondition(pairs.Length == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be empty{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains at least one item.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotBeEmpty(string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (Subject is null) return FailNull("not to be empty", because, becauseArgs);

        Assert().ForCondition(pairs.Length > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be empty{reason}.");
        return new(this);
    }

    /// <summary>Asserts the dictionary contains exactly <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> HaveCount(int expected, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (Subject is null) return FailNull($"to contain {expected} item(s)", because, becauseArgs);

        Assert().ForCondition(pairs.Length == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0} item(s){reason}, but found {1}: {2}.", expected, pairs.Length, Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the dictionary does not contain exactly <paramref name="unexpected"/> items. Says
    /// nothing about the direction of the difference, so prefer an explicit count assertion when
    /// that is what the test really means.
    /// </summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotHaveCount(int unexpected, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (Subject is null) return FailNull($"not to contain {unexpected} item(s)", because, becauseArgs);

        Assert().ForCondition(pairs.Length != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0} item(s){reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains the given key, and exposes its value via Which.</summary>
    public AndWhichConstraint<GenericDictionaryAssertions<TKey, TValue>, TValue> ContainKey(TKey expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null)
        {
            FailNull($"to contain key {Formatting.Formatter.Format(expected)}", because, becauseArgs);
            return new(this, default!);
        }

        if (TryGetValueForKey(expected, out var found))
            return new(this, found);

        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain key {0}{reason}, but found {1}.", expected, Subject);
        return new(this, default!);
    }

    /// <summary>Asserts the dictionary contains all the given keys.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> ContainKeys(params TKey[] expected)
        => ContainKeys((IEnumerable<TKey>)expected, null);

    /// <summary>Asserts the dictionary contains all the given keys.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> ContainKeys(IEnumerable<TKey> expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var expectedKeys = expected as IReadOnlyList<TKey> ?? [.. expected];
        if (Subject is null) return FailNull($"to contain keys {Formatting.Formatter.Format(expectedKeys)}", because, becauseArgs);

        List<TKey>? missingKeys = null;  // allocated only when the assertion is going to fail
        foreach (var key in expectedKeys)
        {
            if (!TryGetValueForKey(key, out _)) (missingKeys ??= []).Add(key);
        }

        Assert().ForCondition(missingKeys is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain keys {0}{reason}, but could not find key(s) {1}.", expectedKeys, missingKeys);
        return new(this);
    }

    /// <summary>Asserts the dictionary does not contain the given key.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContainKey(TKey unexpected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"not to contain key {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);

        var found = TryGetValueForKey(unexpected, out _);

        Assert().ForCondition(!found).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain key {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains none of the given keys.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContainKeys(params TKey[] unexpected)
        => NotContainKeys((IEnumerable<TKey>)unexpected, null);

    /// <summary>Asserts the dictionary contains none of the given keys.</summary>
    /// <remarks>
    /// <para>
    /// This is <see cref="NotContainKey"/> applied to every key, not the strict logical negation of
    /// <see cref="ContainKeys(IEnumerable{TKey}, string?, object?[])"/> — which would be satisfied by
    /// merely <em>one</em> key being absent while the rest are present, an assertion nobody wants to
    /// write. So a dictionary holding some but not all of the given keys fails here.
    /// </para>
    /// <para>
    /// Lookups go through the subject's own equality comparer, so a dictionary built with
    /// <c>StringComparer.OrdinalIgnoreCase</c> correctly reports <c>"ALICE"</c> as present when it
    /// holds <c>"alice"</c>.
    /// </para>
    /// </remarks>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContainKeys(IEnumerable<TKey> unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var unexpectedKeys = unexpected as IReadOnlyList<TKey> ?? [.. unexpected];
        if (Subject is null) return FailNull($"not to contain keys {Formatting.Formatter.Format(unexpectedKeys)}", because, becauseArgs);

        List<TKey>? presentKeys = null;  // allocated only when the assertion is going to fail
        foreach (var key in unexpectedKeys)
        {
            if (TryGetValueForKey(key, out _)) (presentKeys ??= []).Add(key);
        }

        Assert().ForCondition(presentKeys is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain keys {0}{reason}, but found key(s) {1}.", unexpectedKeys, presentKeys);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains the given value.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> ContainValue(TValue expected, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (Subject is null) return FailNull($"to contain value {Formatting.Formatter.Format(expected)}", because, becauseArgs);

        var comparer = EqualityComparer<TValue>.Default;
        var found = false;
        foreach (var pair in pairs)
        {
            if (comparer.Equals(pair.Value, expected)) { found = true; break; }
        }

        Assert().ForCondition(found).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain value {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains all the given values.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> ContainValues(params TValue[] expected)
        => ContainValues((IEnumerable<TValue>)expected, null);

    /// <summary>Asserts the dictionary contains all the given values.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> ContainValues(IEnumerable<TValue> expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var pairs = Pairs;
        var expectedValues = expected as IReadOnlyList<TValue> ?? [.. expected];
        if (Subject is null) return FailNull($"to contain values {Formatting.Formatter.Format(expectedValues)}", because, becauseArgs);

        var presentValues = new HashSet<TValue>();
        foreach (var pair in pairs)
        {
            presentValues.Add(pair.Value);
        }

        List<TValue>? missingValues = null;  // allocated only when the assertion is going to fail
        foreach (var value in expectedValues)
        {
            if (!presentValues.Contains(value)) (missingValues ??= []).Add(value);
        }

        Assert().ForCondition(missingValues is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain values {0}{reason}, but could not find value(s) {1}.", expectedValues, missingValues);
        return new(this);
    }

    /// <summary>Asserts the dictionary does not contain the given value.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContainValue(TValue unexpected, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (Subject is null) return FailNull($"not to contain value {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);

        var comparer = EqualityComparer<TValue>.Default;
        var found = false;
        foreach (var pair in pairs)
        {
            if (comparer.Equals(pair.Value, unexpected)) { found = true; break; }
        }

        Assert().ForCondition(!found).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain value {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains none of the given values.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContainValues(params TValue[] unexpected)
        => NotContainValues((IEnumerable<TValue>)unexpected, null);

    /// <summary>Asserts the dictionary contains none of the given values.</summary>
    /// <remarks>
    /// As with <see cref="NotContainKeys(IEnumerable{TKey}, string?, object?[])"/> this is
    /// <see cref="NotContainValue"/> applied to every value rather than the strict negation of
    /// <see cref="ContainValues(IEnumerable{TValue}, string?, object?[])"/>: a dictionary holding
    /// even one of them fails. Values, unlike keys, are always compared with
    /// <see cref="EqualityComparer{T}.Default"/> — a dictionary's own comparer applies to its keys
    /// only.
    /// </remarks>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContainValues(IEnumerable<TValue> unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var pairs = Pairs;
        var unexpectedValues = unexpected as IReadOnlyList<TValue> ?? [.. unexpected];
        if (Subject is null) return FailNull($"not to contain values {Formatting.Formatter.Format(unexpectedValues)}", because, becauseArgs);

        var comparer = EqualityComparer<TValue>.Default;
        List<TValue>? presentValues = null;  // allocated only when the assertion is going to fail
        foreach (var value in unexpectedValues)
        {
            foreach (var pair in pairs)
            {
                if (comparer.Equals(pair.Value, value)) { (presentValues ??= []).Add(value); break; }
            }
        }

        Assert().ForCondition(presentValues is null).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain values {0}{reason}, but found value(s) {1}.", unexpectedValues, presentValues);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains the given value at the given key.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> Contain(TKey key, TValue value, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to contain {Formatting.Formatter.Format(value)} at key {Formatting.Formatter.Format(key)}", because, becauseArgs);

        if (!TryGetValueForKey(key, out var actual))
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to contain {0} at key {1}{reason}, but the key was not found.", value, key);
            return new(this);
        }

        Assert().ForCondition(EqualityComparer<TValue>.Default.Equals(actual, value)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0} at key {1}{reason}, but found {2}.", value, key, actual);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains the given key/value pair.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> Contain(KeyValuePair<TKey, TValue> expected, string? because = null, params object?[] becauseArgs)
        => Contain(expected.Key, expected.Value, because, becauseArgs);

    /// <summary>Asserts the dictionary does not contain the given value at the given key.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContain(TKey key, TValue value, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"not to contain {Formatting.Formatter.Format(value)} at key {Formatting.Formatter.Format(key)}", because, becauseArgs);

        var found = TryGetValueForKey(key, out var actual)
            && EqualityComparer<TValue>.Default.Equals(actual, value);

        Assert().ForCondition(!found).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0} at key {1}{reason}.", value, key);
        return new(this);
    }

    /// <summary>
    /// Asserts the dictionary does not hold the given key/value pair. Mirrors
    /// <see cref="Contain(KeyValuePair{TKey, TValue}, string?, object?[])"/> by forwarding to the
    /// two-argument form, so an absent key satisfies it just as a different value at that key does.
    /// </summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContain(KeyValuePair<TKey, TValue> unexpected, string? because = null, params object?[] becauseArgs)
        => NotContain(unexpected.Key, unexpected.Value, because, becauseArgs);

    private static TValue FirstValueFor(ReadOnlySpan<KeyValuePair<TKey, TValue>> pairs, TKey key)
    {
        var comparer = EqualityComparer<TKey>.Default;
        foreach (var pair in pairs)
        {
            if (comparer.Equals(pair.Key, key)) return pair.Value;
        }
        return default!;
    }
}
