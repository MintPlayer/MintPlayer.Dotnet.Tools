using MintPlayer.Assertions.Collections;
using MintPlayer.Assertions.Formatting;

// Root namespace, not MintPlayer.Assertions.Collections, and for the same reason AssertionScope is
// here: these are EXTENSION methods, so unlike every instance assertion in the library they are
// invisible until their namespace is imported. Leaving them under .Collections would mean
// `strings.Should().ContainMatch(...)` did not compile after the one using the README promises
// covers everything. That promise is load-bearing and is itself covered by a test.
namespace MintPlayer.Assertions;

/// <summary>
/// Assertions that only make sense on a collection of strings: wildcard matching, casing-insensitive
/// membership, and emptiness of the strings themselves rather than of the collection.
/// </summary>
/// <remarks>
/// ⚠️ <b>Extension methods on <c>GenericCollectionAssertions&lt;string&gt;</c>, deliberately, rather
/// than a <c>StringCollectionAssertions</c> subclass reached from a <c>Should()</c> overload.</b>
/// <para>
/// A subclass is how other libraries do this, and it would need two things this one cannot afford.
/// First, a <c>Should(this IEnumerable&lt;string&gt;)</c> overload — which does NOT capture
/// <c>string[]</c>, because the existing <c>Should&lt;T&gt;(this T[])</c> is the better match for an
/// array, so the surface would silently differ between <c>string[]</c> and <c>List&lt;string&gt;</c>.
/// Second, a self-type parameter on <c>GenericCollectionAssertions</c> so that <c>.And</c> after a
/// base assertion still returns the string-aware type — which is a breaking rewrite of a shipped
/// generic.
/// </para>
/// <para>
/// Extensions have neither problem: they attach to every string collection however it was typed, and
/// <c>.And</c> keeps working because nothing about the chain changed.
/// </para>
/// <para>
/// ⚠️ They read <c>SubjectSpan</c>, never <c>Subject</c>. Enumerating the interface-typed subject
/// boxes a struct enumerator on every call, and MPA0005 does not report it here —
/// <c>IEnumerable&lt;T&gt;</c> is not indexable, so it falls outside that rule's scope. The cost is
/// real whether or not an analyzer names it.
/// </para>
/// </remarks>
public static class StringCollectionAssertions
{
    /// <summary>Asserts at least one string matches the wildcard <paramref name="pattern"/> ('*' and '?').</summary>
    public static AndConstraint<GenericCollectionAssertions<string>> ContainMatch(
        this GenericCollectionAssertions<string> assertions, string pattern, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        if (assertions.Subject is null) return FailNull(assertions, $"to contain a match of {Formatter.Format(pattern)}", because, becauseArgs);
        var matched = CountMatches(assertions.SubjectSpan, pattern);

        assertions.Assert().ForCondition(matched > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain a match of {0}{reason}, but found none in {1}.", pattern, assertions.Subject);
        return new(assertions);
    }

    /// <summary>Asserts no string matches the wildcard <paramref name="pattern"/>.</summary>
    public static AndConstraint<GenericCollectionAssertions<string>> NotContainMatch(
        this GenericCollectionAssertions<string> assertions, string pattern, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        if (assertions.Subject is null) return FailNull(assertions, $"not to contain a match of {Formatter.Format(pattern)}", because, becauseArgs);
        var matched = CountMatches(assertions.SubjectSpan, pattern);

        assertions.Assert().ForCondition(matched == 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain a match of {0}{reason}, but found {1}.", pattern, matched);
        return new(assertions);
    }

    /// <summary>Asserts the number of strings matching <paramref name="pattern"/> satisfies <paramref name="occurrence"/>.</summary>
    public static AndConstraint<GenericCollectionAssertions<string>> ContainMatch(
        this GenericCollectionAssertions<string> assertions, string pattern, OccurrenceConstraint occurrence, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        if (assertions.Subject is null) return FailNull(assertions, $"to contain a match of {Formatter.Format(pattern)} {occurrence}", because, becauseArgs);
        var matched = CountMatches(assertions.SubjectSpan, pattern);

        if (!occurrence.IsSatisfiedBy(matched))
        {
            assertions.Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Expected {{subject}} to contain a match of {{0}} {occurrence}{{reason}}, but found {{1}}.", pattern, matched);
        }
        return new(assertions);
    }

    /// <summary>Asserts the collection contains <paramref name="expected"/>, ignoring casing.</summary>
    public static AndConstraint<GenericCollectionAssertions<string>> ContainEquivalentOf(
        this GenericCollectionAssertions<string> assertions, string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        ArgumentNullException.ThrowIfNull(expected);

        if (assertions.Subject is null) return FailNull(assertions, $"to contain the equivalent of {Formatter.Format(expected)}", because, becauseArgs);

        var items = assertions.SubjectSpan;
        var found = false;
        for (var i = 0; i < items.Length; i++)
        {
            if (string.Equals(items[i], expected, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
        }

        assertions.Assert().ForCondition(found).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain the equivalent of {0}{reason}, but found {1}.", expected, assertions.Subject);
        return new(assertions);
    }

    /// <summary>Asserts no string in the collection is null, empty or white space.</summary>
    /// <remarks>
    /// Distinct from <c>NotContainNulls</c>, which only rejects nulls. The difference matters
    /// wherever a blank string is as wrong as a missing one, which is most of the time.
    /// </remarks>
    public static AndConstraint<GenericCollectionAssertions<string>> NotContainNullsOrWhiteSpace(
        this GenericCollectionAssertions<string> assertions, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        if (assertions.Subject is null) return FailNull(assertions, "not to contain null or white-space items", because, becauseArgs);

        // Lazy: a collection with no blanks is the passing case and must not allocate a list for it.
        var items = assertions.SubjectSpan;
        List<int>? offending = null;
        for (var i = 0; i < items.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(items[i])) (offending ??= []).Add(i);
        }

        assertions.Assert().ForCondition(offending is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to contain null or white-space items{reason}, but found some at index(es) {0}.", offending);
        return new(assertions);
    }

    /// <summary>Asserts every string in the collection starts with <paramref name="prefix"/>.</summary>
    public static AndConstraint<GenericCollectionAssertions<string>> AllStartWith(
        this GenericCollectionAssertions<string> assertions, string prefix, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        if (assertions.Subject is null) return FailNull(assertions, $"to all start with {Formatter.Format(prefix)}", because, becauseArgs);

        var items = assertions.SubjectSpan;
        List<string>? offending = null;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i] is null || !items[i].StartsWith(prefix, StringComparison.Ordinal)) (offending ??= []).Add(items[i]);
        }

        assertions.Assert().ForCondition(offending is null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to all start with {0}{reason}, but {1} do(es) not.", prefix, offending);
        return new(assertions);
    }

    private static int CountMatches(ReadOnlySpan<string> items, string pattern)
    {
        var matched = 0;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i] is not null && WildcardPattern.IsMatch(items[i], pattern)) matched++;
        }
        return matched;
    }

    private static AndConstraint<GenericCollectionAssertions<string>> FailNull(
        GenericCollectionAssertions<string> assertions, string expectation, string? because, object?[] becauseArgs)
        => assertions.FailNullExternally(expectation, because, becauseArgs);
}
