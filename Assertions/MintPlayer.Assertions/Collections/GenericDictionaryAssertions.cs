using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Collections;

/// <summary>
/// Assertions on dictionaries and any other sequence of key/value pairs: emptiness, counts and
/// key/value/pair membership. The subject is materialized at most once per assertions instance,
/// so lazily-evaluated sequences are never enumerated multiple times.
/// </summary>
public class GenericDictionaryAssertions<TKey, TValue> : ReferenceTypeAssertions<IEnumerable<KeyValuePair<TKey, TValue>>, GenericDictionaryAssertions<TKey, TValue>>
{
    private IReadOnlyList<KeyValuePair<TKey, TValue>>? pairs;
    private bool materialized;

    public GenericDictionaryAssertions(IEnumerable<KeyValuePair<TKey, TValue>>? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>The subject materialized into a list exactly once (null when the subject is null).</summary>
    private IReadOnlyList<KeyValuePair<TKey, TValue>>? Pairs
    {
        get
        {
            if (!materialized)
            {
                // Already a random-access collection? Use it. Copying allocated a fresh list
                // proportional to the subject's size on every assertion, passing or failing, and
                // the copy was read once and discarded. Anything else is still copied, which is
                // the case that matters: a lazy sequence must not be re-enumerated per assertion.
                pairs = Subject switch
                {
                    null => null,
                    IReadOnlyList<KeyValuePair<TKey, TValue>> alreadyAList => alreadyAList,
                    _ => [.. Subject],
                };
                materialized = true;
            }
            return pairs;
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
        if (key is not null)
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
        var source = Pairs ?? [];
        // for-loop with if-statement uses less memory allocations than a Where LINQ statement
        for (var i = 0; i < source.Count; i++)
        {
            var pair = source[i];
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
        if (pairs is null) return FailNull("to be empty", because, becauseArgs);

        Assert().ForCondition(pairs.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be empty{reason}, but found {0}.", pairs);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains at least one item.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotBeEmpty(string? because = null, params object?[] becauseArgs)
    {
        if (!TryGetCount(out var actualCount)) return FailNull("not to be empty", because, becauseArgs);

        Assert().ForCondition(actualCount > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be empty{reason}.");
        return new(this);
    }

    /// <summary>Asserts the dictionary contains exactly <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> HaveCount(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (!TryGetCount(out var actualCount)) return FailNull($"to contain {expected} item(s)", because, becauseArgs);

        Assert().ForCondition(actualCount == expected).BecauseOf(because, becauseArgs)
            // Subject, not Pairs: an argument expression is evaluated BEFORE the call, so
            // naming Pairs here materialised the whole dictionary on every passing
            // assertion to build a message that was never rendered. Subject is already in
            // hand and the formatter walks it the same way.
            .FailWith("Expected {subject} to contain {0} item(s){reason}, but found {1}: {2}.", expected, actualCount, Subject);
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
        if (pairs is null) return FailNull($"not to contain {unexpected} item(s)", because, becauseArgs);

        Assert().ForCondition(pairs.Count != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0} item(s){reason}, but found {1}.", unexpected, pairs);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains the given key, and exposes its value via Which.</summary>
    public AndWhichConstraint<GenericDictionaryAssertions<TKey, TValue>, TValue> ContainKey(TKey expected, string? because = null, params object?[] becauseArgs)
    {
        // Null-check the SUBJECT, not Pairs: touching Pairs materialised the whole dictionary on
        // every call, and TryGetValueForKey does not need it. Pairs is only reached below, to render
        // the failure.
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
        var pairs = Pairs;
        var expectedKeys = expected as IReadOnlyList<TKey> ?? [.. expected];
        if (pairs is null) return FailNull($"to contain keys {Formatting.Formatter.Format(expectedKeys)}", because, becauseArgs);

        var missingKeys = new List<TKey>();
        for (var i = 0; i < expectedKeys.Count; i++)
        {
            var key = expectedKeys[i];
            if (!TryGetValueForKey(key, out _)) missingKeys.Add(key);
        }

        Assert().ForCondition(missingKeys.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain keys {0}{reason}, but could not find key(s) {1}.", expectedKeys, missingKeys);
        return new(this);
    }

    /// <summary>Asserts the dictionary does not contain the given key.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContainKey(TKey unexpected, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (pairs is null) return FailNull($"not to contain key {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);

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
        var pairs = Pairs;
        var unexpectedKeys = unexpected as IReadOnlyList<TKey> ?? [.. unexpected];
        if (pairs is null) return FailNull($"not to contain keys {Formatting.Formatter.Format(unexpectedKeys)}", because, becauseArgs);

        var presentKeys = new List<TKey>();
        for (var i = 0; i < unexpectedKeys.Count; i++)
        {
            var key = unexpectedKeys[i];
            if (TryGetValueForKey(key, out _)) presentKeys.Add(key);
        }

        Assert().ForCondition(presentKeys.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain keys {0}{reason}, but found key(s) {1}.", unexpectedKeys, presentKeys);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains the given value.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> ContainValue(TValue expected, string? because = null, params object?[] becauseArgs)
    {
        if (Subject is null) return FailNull($"to contain value {Formatting.Formatter.Format(expected)}", because, becauseArgs);

        Assert().ForCondition(HoldsValue(expected)).BecauseOf(because, becauseArgs)
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
        if (pairs is null) return FailNull($"to contain values {Formatting.Formatter.Format(expectedValues)}", because, becauseArgs);

        var presentValues = new HashSet<TValue>();
        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            presentValues.Add(pair.Value);
        }

        var missingValues = new List<TValue>();
        for (var i = 0; i < expectedValues.Count; i++)
        {
            var value = expectedValues[i];
            if (!presentValues.Contains(value)) missingValues.Add(value);
        }

        Assert().ForCondition(missingValues.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain values {0}{reason}, but could not find value(s) {1}.", expectedValues, missingValues);
        return new(this);
    }

    /// <summary>Asserts the dictionary does not contain the given value.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContainValue(TValue unexpected, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (pairs is null) return FailNull($"not to contain value {Formatting.Formatter.Format(unexpected)}", because, becauseArgs);

        var comparer = EqualityComparer<TValue>.Default;
        var found = false;
        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
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
        if (pairs is null) return FailNull($"not to contain values {Formatting.Formatter.Format(unexpectedValues)}", because, becauseArgs);

        var comparer = EqualityComparer<TValue>.Default;
        var presentValues = new List<TValue>();
        for (var i = 0; i < unexpectedValues.Count; i++)
        {
            var value = unexpectedValues[i];
            for (var j = 0; j < pairs.Count; j++)
            {
                var pair = pairs[j];
                if (comparer.Equals(pair.Value, value)) { presentValues.Add(value); break; }
            }
        }

        Assert().ForCondition(presentValues.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain values {0}{reason}, but found value(s) {1}.", unexpectedValues, presentValues);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains the given value at the given key.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> Contain(TKey key, TValue value, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (pairs is null) return FailNull($"to contain {Formatting.Formatter.Format(value)} at key {Formatting.Formatter.Format(key)}", because, becauseArgs);

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
        var pairs = Pairs;
        if (pairs is null) return FailNull($"not to contain {Formatting.Formatter.Format(value)} at key {Formatting.Formatter.Format(key)}", because, becauseArgs);

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

    private static TValue FirstValueFor(IReadOnlyList<KeyValuePair<TKey, TValue>> pairs, TKey key)
    {
        var comparer = EqualityComparer<TKey>.Default;
        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            if (comparer.Equals(pair.Key, key)) return pair.Value;
        }
        return default!;
    }

    /// <summary>Asserts the dictionary holds exactly the same key/value pairs as <paramref name="expected"/>, regardless of order.</summary>
    /// <remarks>
    /// Order-insensitive on purpose: a dictionary does not promise one, so an order-sensitive
    /// equality here would report failures that depend on hashing rather than on the data.
    /// </remarks>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> Equal(IEnumerable<KeyValuePair<TKey, TValue>> expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var actual = Pairs;
        if (actual is null) return FailNull("to equal the expected dictionary", because, becauseArgs);

        var expectedPairs = expected as IReadOnlyList<KeyValuePair<TKey, TValue>> ?? [.. expected];

        var missing = new List<TKey>();
        for (var i = 0; i < expectedPairs.Count; i++)
        {
            var pair = expectedPairs[i];
            if (!TryGetValueForKey(pair.Key, out var value) || !EqualityComparer<TValue>.Default.Equals(value, pair.Value))
            {
                missing.Add(pair.Key);
            }
        }

        Assert().ForCondition(actual.Count == expectedPairs.Count && missing.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to equal {0}{reason}, but it has {1} pair(s) and differs at key(s) {2}.", expected, actual.Count, missing);
        return new(this);
    }

    /// <summary>Asserts the dictionary does not hold exactly the same key/value pairs as <paramref name="unexpected"/>.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotEqual(IEnumerable<KeyValuePair<TKey, TValue>> unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var actual = Pairs;
        if (actual is null) return FailNull("not to equal the given dictionary", because, becauseArgs);

        var unexpectedPairs = unexpected as IReadOnlyList<KeyValuePair<TKey, TValue>> ?? [.. unexpected];

        var same = actual.Count == unexpectedPairs.Count;
        if (same)
        {
            for (var i = 0; i < unexpectedPairs.Count; i++)
            {
                var pair = unexpectedPairs[i];
                if (!TryGetValueForKey(pair.Key, out var value) || !EqualityComparer<TValue>.Default.Equals(value, pair.Value))
                {
                    same = false;
                    break;
                }
            }
        }

        Assert().ForCondition(!same).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to equal {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains every one of <paramref name="expected"/>.</summary>
    /// <remarks>
    /// The bulk counterpart of the single-pair overloads. Without it, several expected pairs meant
    /// one call each and a message naming only the first one that was absent.
    /// </remarks>
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
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> ContainAll(params KeyValuePair<TKey, TValue>[] expected)
        => ContainAll((IEnumerable<KeyValuePair<TKey, TValue>>)expected, because: null);

    /// <summary>Asserts the dictionary contains every one of <paramref name="expected"/>.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> ContainAll(IEnumerable<KeyValuePair<TKey, TValue>> expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (Pairs is null) return FailNull("to contain the expected pairs", because, becauseArgs);

        var missing = new List<TKey>();
        foreach (var pair in expected)
        {
            if (!TryGetValueForKey(pair.Key, out var value) || !EqualityComparer<TValue>.Default.Equals(value, pair.Value))
            {
                missing.Add(pair.Key);
            }
        }

        Assert().ForCondition(missing.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0}{reason}, but key(s) {1} are missing or hold a different value.", expected, missing);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains none of <paramref name="unexpected"/>.</summary>
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
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContainAny(params KeyValuePair<TKey, TValue>[] unexpected)
        => NotContainAny((IEnumerable<KeyValuePair<TKey, TValue>>)unexpected, because: null);

    /// <summary>Asserts the dictionary contains none of <paramref name="unexpected"/>.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> NotContainAny(IEnumerable<KeyValuePair<TKey, TValue>> unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        if (Pairs is null) return FailNull("not to contain the given pairs", because, becauseArgs);

        var found = new List<TKey>();
        foreach (var pair in unexpected)
        {
            if (TryGetValueForKey(pair.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, pair.Value))
            {
                found.Add(pair.Key);
            }
        }

        Assert().ForCondition(found.Count == 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0}{reason}, but key(s) {1} are present with that value.", unexpected, found);
        return new(this);
    }

    // The four count comparisons are written out rather than sharing a helper. A helper taking a
    // Func<int, bool> would capture `expected` and allocate a closure on EVERY call, passing or
    // failing, and a helper taking a message fragment would concatenate the template the same way.
    // Both are exactly what the Phase 2 boundary forbids, and four short methods cost nothing.

    /// <summary>Asserts the dictionary has more than <paramref name="expected"/> pairs.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> HaveCountGreaterThan(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (!TryGetCount(out var actualCount)) return FailNull("to have more pairs than expected", because, becauseArgs);

        Assert().ForCondition(actualCount > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have more than {0} pair(s){reason}, but found {1}.", expected, actualCount);
        return new(this);
    }

    /// <summary>Asserts the dictionary has at least <paramref name="expected"/> pairs.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> HaveCountGreaterThanOrEqualTo(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (!TryGetCount(out var actualCount)) return FailNull("to have at least the expected number of pairs", because, becauseArgs);

        Assert().ForCondition(actualCount >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have at least {0} pair(s){reason}, but found {1}.", expected, actualCount);
        return new(this);
    }

    /// <summary>Asserts the dictionary has fewer than <paramref name="expected"/> pairs.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> HaveCountLessThan(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (!TryGetCount(out var actualCount)) return FailNull("to have fewer pairs than expected", because, becauseArgs);

        Assert().ForCondition(actualCount < expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have fewer than {0} pair(s){reason}, but found {1}.", expected, actualCount);
        return new(this);
    }

    /// <summary>Asserts the dictionary has at most <paramref name="expected"/> pairs.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> HaveCountLessThanOrEqualTo(int expected, string? because = null, params object?[] becauseArgs)
    {
        if (!TryGetCount(out var actualCount)) return FailNull("to have at most the expected number of pairs", because, becauseArgs);

        Assert().ForCondition(actualCount <= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have at most {0} pair(s){reason}, but found {1}.", expected, actualCount);
        return new(this);
    }

    /// <summary>
    /// The subject's pair count, without materialising it when the subject can report it itself.
    /// </summary>
    /// <remarks>
    /// This exists because <see cref="Pairs"/> could not help here: <c>Dictionary&lt;TKey,TValue&gt;</c>
    /// does NOT implement <c>IReadOnlyList&lt;KeyValuePair&lt;TKey,TValue&gt;&gt;</c>, so the
    /// "already a list, use it as-is" fast path never applied to the most common subject of all, and
    /// every count-only assertion copied the whole dictionary just to read <c>Count</c> — measured at
    /// 144 bytes per assertion on a two-entry dictionary, on the passing path.
    ///
    /// Returns false only when the subject is null, which the caller reports.
    /// </remarks>
    private bool TryGetCount(out int count)
    {
        switch (Subject)
        {
            case null:
                count = 0;
                return false;
            case ICollection<KeyValuePair<TKey, TValue>> collection:
                count = collection.Count;
                return true;
            case IReadOnlyCollection<KeyValuePair<TKey, TValue>> readOnly:
                count = readOnly.Count;
                return true;
            default:
                count = Pairs!.Count;
                return true;
        }
    }

    /// <summary>
    /// Whether any pair holds <paramref name="value"/>, without materialising a
    /// <c>Dictionary&lt;TKey,TValue&gt;</c> subject.
    /// </summary>
    /// <remarks>
    /// The Dictionary case is special-cased because its enumerator is a struct and iterating the
    /// concrete type never boxes it. Anything else falls back to the materialised list and an
    /// indexed loop — foreach there would iterate through IReadOnlyList and box.
    /// </remarks>
    private bool HoldsValue(TValue value)
    {
        var comparer = EqualityComparer<TValue>.Default;

        if (Subject is Dictionary<TKey, TValue> dictionary)
        {
            foreach (var pair in dictionary)
            {
                if (comparer.Equals(pair.Value, value)) return true;
            }

            return false;
        }

        var pairs = Pairs!;
        for (var i = 0; i < pairs.Count; i++)
        {
            if (comparer.Equals(pairs[i].Value, value)) return true;
        }

        return false;
    }
}
