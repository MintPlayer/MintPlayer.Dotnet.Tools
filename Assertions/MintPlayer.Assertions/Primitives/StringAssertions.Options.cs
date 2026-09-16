namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// The <see cref="StringMatchOptions"/> overloads: the same assertions, with casing, whitespace and
/// newline style under the caller's control.
/// </summary>
/// <remarks>
/// <para>
/// These live beside the ordinal forms rather than replacing them. The ordinal ones are the common
/// case and stay allocation-free; passing <see cref="StringMatchOptions.None"/> here is exactly
/// equivalent to calling them, and passing only <see cref="StringMatchOptions.IgnoringCase"/> is
/// equivalent to the <c>EquivalentOf</c> variants.
/// </para>
/// <para>
/// The failure messages name the options, because a string comparison that fails while whitespace is
/// supposedly being ignored is otherwise baffling to read.
/// </para>
/// </remarks>
public partial class StringAssertions
{
    /// <summary>Asserts equality under <paramref name="options"/>.</summary>
    public AndConstraint<StringAssertions> Be(string? expected, StringMatchOptions options, string? because = null, params object?[] becauseArgs)
    {
        var subject = StringMatch.Normalize(Subject, options);
        var normalizedExpected = StringMatch.Normalize(expected, options);

        Assert().ForCondition(string.Equals(subject, normalizedExpected, StringMatch.Comparison(options))).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason} (comparing with {1}), but found {2}.", expected, options, Subject);
        return new(this);
    }

    /// <summary>Asserts inequality under <paramref name="options"/>.</summary>
    public AndConstraint<StringAssertions> NotBe(string? unexpected, StringMatchOptions options, string? because = null, params object?[] becauseArgs)
    {
        var subject = StringMatch.Normalize(Subject, options);
        var normalizedUnexpected = StringMatch.Normalize(unexpected, options);

        Assert().ForCondition(!string.Equals(subject, normalizedUnexpected, StringMatch.Comparison(options))).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason} (comparing with {1}).", unexpected, options);
        return new(this);
    }

    /// <summary>Asserts the subject starts with <paramref name="expected"/> under <paramref name="options"/>.</summary>
    /// <remarks>
    /// Normalisation happens before the prefix test, so
    /// <see cref="StringMatchOptions.IgnoringTrailingWhitespace"/> trims the *expectation's* trailing
    /// whitespace rather than the subject's tail — which is the only reading that makes sense for a
    /// prefix.
    /// </remarks>
    public AndConstraint<StringAssertions> StartWith(string expected, StringMatchOptions options, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var subject = StringMatch.Normalize(Subject, options);
        var normalizedExpected = StringMatch.Normalize(expected, options)!;

        Assert().ForCondition(subject is not null && subject.StartsWith(normalizedExpected, StringMatch.Comparison(options))).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to start with {0}{reason} (comparing with {1}), but found {2}.", expected, options, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not start with <paramref name="unexpected"/> under <paramref name="options"/>.</summary>
    public AndConstraint<StringAssertions> NotStartWith(string unexpected, StringMatchOptions options, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var subject = StringMatch.Normalize(Subject, options);
        var normalizedUnexpected = StringMatch.Normalize(unexpected, options)!;

        Assert().ForCondition(subject is null || !subject.StartsWith(normalizedUnexpected, StringMatch.Comparison(options))).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to start with {0}{reason} (comparing with {1}).", unexpected, options);
        return new(this);
    }

    /// <summary>Asserts the subject ends with <paramref name="expected"/> under <paramref name="options"/>.</summary>
    public AndConstraint<StringAssertions> EndWith(string expected, StringMatchOptions options, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var subject = StringMatch.Normalize(Subject, options);
        var normalizedExpected = StringMatch.Normalize(expected, options)!;

        Assert().ForCondition(subject is not null && subject.EndsWith(normalizedExpected, StringMatch.Comparison(options))).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to end with {0}{reason} (comparing with {1}), but found {2}.", expected, options, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not end with <paramref name="unexpected"/> under <paramref name="options"/>.</summary>
    public AndConstraint<StringAssertions> NotEndWith(string unexpected, StringMatchOptions options, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var subject = StringMatch.Normalize(Subject, options);
        var normalizedUnexpected = StringMatch.Normalize(unexpected, options)!;

        Assert().ForCondition(subject is null || !subject.EndsWith(normalizedUnexpected, StringMatch.Comparison(options))).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to end with {0}{reason} (comparing with {1}).", unexpected, options);
        return new(this);
    }

    /// <summary>Asserts the subject contains <paramref name="expected"/> under <paramref name="options"/>.</summary>
    /// <remarks>
    /// The empty-expectation guard from the ordinal overload is kept, and it bites harder here:
    /// <see cref="StringMatchOptions.IgnoringAllWhitespace"/> turns <c>" "</c> into the empty string,
    /// which every subject contains. Rejecting that up front is better than passing vacuously.
    /// </remarks>
    public AndConstraint<StringAssertions> Contain(string expected, StringMatchOptions options, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(expected);
        var subject = StringMatch.Normalize(Subject, options);
        var normalizedExpected = StringMatch.Normalize(expected, options)!;
        ArgumentException.ThrowIfNullOrEmpty(normalizedExpected, nameof(expected));

        Assert().ForCondition(subject is not null && subject.Contains(normalizedExpected, StringMatch.Comparison(options))).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to contain {0}{reason} (comparing with {1}), but found {2}.", expected, options, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not contain <paramref name="unexpected"/> under <paramref name="options"/>.</summary>
    public AndConstraint<StringAssertions> NotContain(string unexpected, StringMatchOptions options, string? because = null, params object?[] becauseArgs)
    {
        ArgumentException.ThrowIfNullOrEmpty(unexpected);
        var subject = StringMatch.Normalize(Subject, options);
        var normalizedUnexpected = StringMatch.Normalize(unexpected, options)!;
        ArgumentException.ThrowIfNullOrEmpty(normalizedUnexpected, nameof(unexpected));

        Assert().ForCondition(subject is null || !subject.Contains(normalizedUnexpected, StringMatch.Comparison(options))).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to contain {0}{reason} (comparing with {1}), but found {2}.", unexpected, options, Subject);
        return new(this);
    }
}
