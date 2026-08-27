using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MintPlayer.Assertions.Formatting;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on strings: equality (ordinal; "EquivalentOf" variants compare ordinal-ignore-case),
/// emptiness and whitespace checks, length, prefix/suffix/substring checks, wildcard and regex
/// matching, and casing. Negative assertions treat a null subject as passing (null starts with,
/// ends with and contains nothing); positive assertions fail on null.
/// </summary>
public class StringAssertions : ReferenceTypeAssertions<string, StringAssertions>
{
    public StringAssertions(string? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    /// <summary>Asserts ordinal equality; on mismatch the message points at the first differing index.</summary>
    public AndConstraint<StringAssertions> Be(string? expected, string? because = null, params object?[] becauseArgs)
    {
        var equal = string.Equals(Subject, expected, StringComparison.Ordinal);
        if (!equal && Subject is not null && expected is not null)
        {
            // The hint is woven into the template itself; escape '{' so excerpts of the
            // compared strings can never be mistaken for placeholders.
            var hint = StringDifference.Describe(Subject, expected).Replace("{", "{{");
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to be {0}{reason}, but " + hint + ".", expected);
        }
        else
        {
            Assert().ForCondition(equal).BecauseOf(because, becauseArgs)
                .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        }
        return new(this);
    }

    /// <summary>Asserts the subject is not ordinally equal to <paramref name="unexpected"/>.</summary>
    public AndConstraint<StringAssertions> NotBe(string? unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!string.Equals(Subject, unexpected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts equality ignoring casing (ordinal-ignore-case).</summary>
    public AndConstraint<StringAssertions> BeEquivalentTo(string? expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(string.Equals(Subject, expected, StringComparison.OrdinalIgnoreCase)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be equivalent to {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not equal to <paramref name="unexpected"/> ignoring casing.</summary>
    public AndConstraint<StringAssertions> NotBeEquivalentTo(string? unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!string.Equals(Subject, unexpected, StringComparison.OrdinalIgnoreCase)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be equivalent to {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the subject is the empty string.</summary>
    public AndConstraint<StringAssertions> BeEmpty(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { Length: 0 }).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be empty{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not the empty string (a null subject fails too).</summary>
    public AndConstraint<StringAssertions> NotBeEmpty(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be empty{reason}, but found <null>.")
            .ForCondition(Subject is null || Subject.Length > 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be empty{reason}.");
        return new(this);
    }

    /// <summary>Asserts the subject is null or the empty string.</summary>
    public AndConstraint<StringAssertions> BeNullOrEmpty(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(string.IsNullOrEmpty(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be null or empty{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is neither null nor the empty string.</summary>
    public AndConstraint<StringAssertions> NotBeNullOrEmpty(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!string.IsNullOrEmpty(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be null or empty{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is null, empty or consists only of white-space characters.</summary>
    public AndConstraint<StringAssertions> BeNullOrWhiteSpace(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(string.IsNullOrWhiteSpace(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be null or white-space{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not null, not empty and not only white-space.</summary>
    public AndConstraint<StringAssertions> NotBeNullOrWhiteSpace(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!string.IsNullOrWhiteSpace(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be null or white-space{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject has exactly <paramref name="expected"/> characters.</summary>
    public AndConstraint<StringAssertions> HaveLength(int expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have length {0}{reason}, but found <null>.", expected)
            .ForCondition(Subject is null || Subject.Length == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have length {0}{reason}, but {1} has length {2}.", expected, Subject, Subject?.Length);
        return new(this);
    }

    /// <summary>Asserts the subject starts with <paramref name="expected"/> (ordinal).</summary>
    public AndConstraint<StringAssertions> StartWith(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject is not null && Subject.StartsWith(expected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to start with {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not start with <paramref name="unexpected"/> (ordinal).</summary>
    public AndConstraint<StringAssertions> NotStartWith(string unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        Assert().ForCondition(Subject is null || !Subject.StartsWith(unexpected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to start with {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the subject starts with <paramref name="expected"/>, ignoring casing.</summary>
    public AndConstraint<StringAssertions> StartWithEquivalentOf(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject is not null && Subject.StartsWith(expected, StringComparison.OrdinalIgnoreCase)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to start with the equivalent of {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject ends with <paramref name="expected"/> (ordinal).</summary>
    public AndConstraint<StringAssertions> EndWith(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject is not null && Subject.EndsWith(expected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to end with {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not end with <paramref name="unexpected"/> (ordinal).</summary>
    public AndConstraint<StringAssertions> NotEndWith(string unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        Assert().ForCondition(Subject is null || !Subject.EndsWith(unexpected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to end with {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the subject ends with <paramref name="expected"/>, ignoring casing.</summary>
    public AndConstraint<StringAssertions> EndWithEquivalentOf(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert().ForCondition(Subject is not null && Subject.EndsWith(expected, StringComparison.OrdinalIgnoreCase)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to end with the equivalent of {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject contains <paramref name="expected"/> (ordinal).</summary>
    public AndConstraint<StringAssertions> Contain(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(expected);
        Assert().ForCondition(Subject is not null && Subject.Contains(expected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not contain <paramref name="unexpected"/> (ordinal).</summary>
    public AndConstraint<StringAssertions> NotContain(string unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(unexpected);
        Assert().ForCondition(Subject is null || !Subject.Contains(unexpected, StringComparison.Ordinal)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject contains <paramref name="expected"/>, ignoring casing.</summary>
    public AndConstraint<StringAssertions> ContainEquivalentOf(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(expected);
        Assert().ForCondition(Subject is not null && Subject.Contains(expected, StringComparison.OrdinalIgnoreCase)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain the equivalent of {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not contain <paramref name="unexpected"/>, ignoring casing.</summary>
    public AndConstraint<StringAssertions> NotContainEquivalentOf(string unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(unexpected);
        Assert().ForCondition(Subject is null || !Subject.Contains(unexpected, StringComparison.OrdinalIgnoreCase)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain the equivalent of {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject contains every one of <paramref name="values"/> (ordinal); missing values are listed.</summary>
    public AndConstraint<StringAssertions> ContainAll(params string[] values)
        => ContainAll(values, because: null);

    /// <summary>Asserts the subject contains every one of <paramref name="values"/> (ordinal); missing values are listed.</summary>
    public AndConstraint<StringAssertions> ContainAll(string[] values, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(values);
        var missing = Array.FindAll(values, v => Subject is null || !Subject.Contains(v, StringComparison.Ordinal));
        Assert().ForCondition(missing.Length == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain all of {0}{reason}, but could not find {1} in {2}.", values, missing, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject contains at least one of <paramref name="values"/> (ordinal).</summary>
    public AndConstraint<StringAssertions> ContainAny(params string[] values)
        => ContainAny(values, because: null);

    /// <summary>Asserts the subject contains at least one of <paramref name="values"/> (ordinal).</summary>
    public AndConstraint<StringAssertions> ContainAny(string[] values, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(values);
        var any = Subject is not null && Array.Exists(values, v => Subject.Contains(v, StringComparison.Ordinal));
        Assert().ForCondition(any).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain at least one of {0}{reason}, but found {1}.", values, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject matches the wildcard pattern (<c>*</c> any sequence, <c>?</c> one character; ordinal).</summary>
    public AndConstraint<StringAssertions> Match(string wildcardPattern, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(wildcardPattern);
        Assert().ForCondition(WildcardPattern.IsMatch(Subject, wildcardPattern)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to match {0}{reason}, but found {1}.", wildcardPattern, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not match the wildcard pattern (ordinal).</summary>
    public AndConstraint<StringAssertions> NotMatch(string wildcardPattern, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(wildcardPattern);
        Assert().ForCondition(!WildcardPattern.IsMatch(Subject, wildcardPattern)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to match {0}{reason}, but found {1}.", wildcardPattern, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject matches the wildcard pattern, ignoring casing.</summary>
    public AndConstraint<StringAssertions> MatchEquivalentOf(string wildcardPattern, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(wildcardPattern);
        Assert().ForCondition(WildcardPattern.IsMatch(Subject, wildcardPattern, ignoreCase: true)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to match the equivalent of {0}{reason}, but found {1}.", wildcardPattern, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject matches the regular expression <paramref name="pattern"/>.</summary>
    public AndConstraint<StringAssertions> MatchRegex([StringSyntax(StringSyntaxAttribute.Regex)] string pattern,
        string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Assert().ForCondition(Subject is not null && Regex.IsMatch(Subject, pattern)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to match regex {0}{reason}, but found {1}.", pattern, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not match the regular expression <paramref name="pattern"/>.</summary>
    public AndConstraint<StringAssertions> NotMatchRegex([StringSyntax(StringSyntaxAttribute.Regex)] string pattern,
        string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Assert().ForCondition(Subject is null || !Regex.IsMatch(Subject, pattern)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to match regex {0}{reason}, but found {1}.", pattern, Subject);
        return new(this);
    }

    /// <summary>Asserts every letter in the subject is upper-cased (non-letters are ignored).</summary>
    public AndConstraint<StringAssertions> BeUpperCased(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be upper-cased{reason}, but found <null>.")
            .ForCondition(Subject is null || !ContainsLetterWhere(Subject, char.IsLower)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be upper-cased{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts every letter in the subject is lower-cased (non-letters are ignored).</summary>
    public AndConstraint<StringAssertions> BeLowerCased(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not null).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be lower-cased{reason}, but found <null>.")
            .ForCondition(Subject is null || !ContainsLetterWhere(Subject, char.IsUpper)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be lower-cased{reason}, but found {0}.", Subject);
        return new(this);
    }

    private static bool ContainsLetterWhere(string value, Func<char, bool> predicate)
    {
        foreach (var c in value)
        {
            if (char.IsLetter(c) && predicate(c)) return true;
        }
        return false;
    }
}
