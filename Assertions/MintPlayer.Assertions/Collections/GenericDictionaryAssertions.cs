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
                pairs = Subject is null ? null : [.. Subject];
                materialized = true;
            }
            return pairs;
        }
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
        var pairs = Pairs;
        if (pairs is null) return FailNull("not to be empty", because, becauseArgs);

        Assert().ForCondition(pairs.Count > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be empty{reason}.");
        return new(this);
    }

    /// <summary>Asserts the dictionary contains exactly <paramref name="expected"/> items.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> HaveCount(int expected, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (pairs is null) return FailNull($"to contain {expected} item(s)", because, becauseArgs);

        Assert().ForCondition(pairs.Count == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0} item(s){reason}, but found {1}: {2}.", expected, pairs.Count, pairs);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains the given key, and exposes its value via Which.</summary>
    public AndWhichConstraint<GenericDictionaryAssertions<TKey, TValue>, TValue> ContainKey(TKey expected, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (pairs is null)
        {
            FailNull($"to contain key {Formatting.Formatter.Format(expected)}", because, becauseArgs);
            return new(this, default!);
        }

        var comparer = EqualityComparer<TKey>.Default;
        foreach (var pair in pairs)
        {
            if (comparer.Equals(pair.Key, expected))
                return new(this, pair.Value);
        }

        Assert().ForCondition(false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain key {0}{reason}, but found {1}.", expected, pairs);
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

        var presentKeys = new HashSet<TKey>();
        foreach (var pair in pairs)
        {
            presentKeys.Add(pair.Key);
        }

        var missingKeys = new List<TKey>();
        foreach (var key in expectedKeys)
        {
            if (!presentKeys.Contains(key)) missingKeys.Add(key);
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

        var comparer = EqualityComparer<TKey>.Default;
        var found = false;
        foreach (var pair in pairs)
        {
            if (comparer.Equals(pair.Key, unexpected)) { found = true; break; }
        }

        Assert().ForCondition(!found).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain key {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains the given value.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> ContainValue(TValue expected, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (pairs is null) return FailNull($"to contain value {Formatting.Formatter.Format(expected)}", because, becauseArgs);

        var comparer = EqualityComparer<TValue>.Default;
        var found = false;
        foreach (var pair in pairs)
        {
            if (comparer.Equals(pair.Value, expected)) { found = true; break; }
        }

        Assert().ForCondition(found).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain value {0}{reason}, but found {1}.", expected, pairs);
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
        foreach (var pair in pairs)
        {
            presentValues.Add(pair.Value);
        }

        var missingValues = new List<TValue>();
        foreach (var value in expectedValues)
        {
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
        foreach (var pair in pairs)
        {
            if (comparer.Equals(pair.Value, unexpected)) { found = true; break; }
        }

        Assert().ForCondition(!found).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain value {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the dictionary contains the given value at the given key.</summary>
    public AndConstraint<GenericDictionaryAssertions<TKey, TValue>> Contain(TKey key, TValue value, string? because = null, params object?[] becauseArgs)
    {
        var pairs = Pairs;
        if (pairs is null) return FailNull($"to contain {Formatting.Formatter.Format(value)} at key {Formatting.Formatter.Format(key)}", because, becauseArgs);

        var keyComparer = EqualityComparer<TKey>.Default;
        var valueComparer = EqualityComparer<TValue>.Default;
        var keyFound = false;
        foreach (var pair in pairs)
        {
            if (!keyComparer.Equals(pair.Key, key)) continue;
            keyFound = true;
            if (valueComparer.Equals(pair.Value, value)) return new(this);
        }

        if (!keyFound)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to contain {0} at key {1}{reason}, but the key was not found.", value, key);
        }
        else
        {
            var actual = FirstValueFor(pairs, key);
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to contain {0} at key {1}{reason}, but found {2}.", value, key, actual);
        }
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

        var keyComparer = EqualityComparer<TKey>.Default;
        var valueComparer = EqualityComparer<TValue>.Default;
        var found = false;
        foreach (var pair in pairs)
        {
            if (keyComparer.Equals(pair.Key, key) && valueComparer.Equals(pair.Value, value)) { found = true; break; }
        }

        Assert().ForCondition(!found).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0} at key {1}{reason}.", value, key);
        return new(this);
    }

    private static TValue FirstValueFor(IReadOnlyList<KeyValuePair<TKey, TValue>> pairs, TKey key)
    {
        var comparer = EqualityComparer<TKey>.Default;
        foreach (var pair in pairs)
        {
            if (comparer.Equals(pair.Key, key)) return pair.Value;
        }
        return default!;
    }
}
