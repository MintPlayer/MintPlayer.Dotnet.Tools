using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on <see cref="DateOnly"/> (and <see cref="Nullable{DateOnly}"/>) subjects:
/// equality, ordering, calendar components and null checks.
/// Positive assertions fail on a null subject; negative ones treat null as passing.
/// </summary>
public class DateOnlyAssertions
{
    public DateOnlyAssertions(DateOnly? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test.</summary>
    public DateOnly? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the subject equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateOnlyAssertions> Be(DateOnly expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not equal <paramref name="unexpected"/>.</summary>
    public AndConstraint<DateOnlyAssertions> NotBe(DateOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the subject is strictly before <paramref name="expected"/>.</summary>
    public AndConstraint<DateOnlyAssertions> BeBefore(DateOnly expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject < expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be before {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is on or before <paramref name="expected"/>.</summary>
    public AndConstraint<DateOnlyAssertions> BeOnOrBefore(DateOnly expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject <= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be on or before {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is strictly after <paramref name="expected"/>.</summary>
    public AndConstraint<DateOnlyAssertions> BeAfter(DateOnly expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be after {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is on or after <paramref name="expected"/>.</summary>
    public AndConstraint<DateOnlyAssertions> BeOnOrAfter(DateOnly expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be on or after {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject's year equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateOnlyAssertions> HaveYear(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have year {0}{reason}, but found <null>.", "Expected {subject} to have year {0}{reason}, but found {1}.", expected, Subject?.Year, because, becauseArgs);

    /// <summary>Asserts the subject's month equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateOnlyAssertions> HaveMonth(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have month {0}{reason}, but found <null>.", "Expected {subject} to have month {0}{reason}, but found {1}.", expected, Subject?.Month, because, becauseArgs);

    /// <summary>Asserts the subject's day equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateOnlyAssertions> HaveDay(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have day {0}{reason}, but found <null>.", "Expected {subject} to have day {0}{reason}, but found {1}.", expected, Subject?.Day, because, becauseArgs);

    /// <summary>Asserts the nullable subject has a value.</summary>
    public AndConstraint<DateOnlyAssertions> HaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a value{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the nullable subject has no value.</summary>
    public AndConstraint<DateOnlyAssertions> NotHaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a value{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is one of <paramref name="validValues"/>.</summary>
    public AndConstraint<DateOnlyAssertions> BeOneOf(params DateOnly[] validValues)
        => BeOneOf(validValues, because: null);

    /// <summary>Asserts the subject is one of <paramref name="validValues"/>.</summary>
    public AndConstraint<DateOnlyAssertions> BeOneOf(DateOnly[] validValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(validValues);
        Assert().ForCondition(Subject.HasValue && Array.IndexOf(validValues, Subject.Value) >= 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be one of {0}{reason}, but found {1}.", validValues, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is none of <paramref name="unexpectedValues"/> (a null subject passes).</summary>
    public AndConstraint<DateOnlyAssertions> NotBeOneOf(params DateOnly[] unexpectedValues)
        => NotBeOneOf(unexpectedValues, because: null);

    /// <summary>
    /// Asserts the subject is none of <paramref name="unexpectedValues"/> (a null subject passes).
    /// An empty set passes too: there is nothing for the subject to be one of.
    /// </summary>
    public AndConstraint<DateOnlyAssertions> NotBeOneOf(DateOnly[] unexpectedValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpectedValues);
        Assert().ForCondition(!Subject.HasValue || Array.IndexOf(unexpectedValues, Subject.Value) < 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be one of {0}{reason}, but found {1}.", unexpectedValues, Subject);
        return new(this);
    }

    /// <remarks>
    /// The message templates arrive as literals instead of being built here from a component name.
    /// Concatenating the name into the template ran on EVERY call, passing or failing, putting two
    /// string allocations on the passing path of every component assertion - exactly what the
    /// Phase 2 boundary forbids. Literals cost nothing, and the arguments are only boxed into an
    /// array once a condition has actually failed.
    /// </remarks>
    private AndConstraint<DateOnlyAssertions> HaveComponent(string nullTemplate, string mismatchTemplate, int expected, int? actual, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith(nullTemplate, expected)
            .ForCondition(!Subject.HasValue || actual == expected).BecauseOf(because, becauseArgs)
            .FailWith(mismatchTemplate, expected, actual);
        return new(this);
    }

    // The four negatives below. Each is the logical complement of its positive counterpart, and a
    // null subject PASSES every one of them - consistent with the library's documented rule that a
    // negative assertion is satisfied by the absence of a subject. They exist because
    // NotBeBefore(x) reads as what a test means, where the equivalent BeOnOrAfter(x) makes the
    // reader do the inversion.

    /// <summary>Asserts the subject is not before <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateOnlyAssertions> NotBeBefore(DateOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject < unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be before {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not on or before <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateOnlyAssertions> NotBeOnOrBefore(DateOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject <= unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on or before {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not after <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateOnlyAssertions> NotBeAfter(DateOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject > unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be after {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not on or after <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateOnlyAssertions> NotBeOnOrAfter(DateOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject >= unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on or after {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject's year does not equal <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateOnlyAssertions> NotHaveYear(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have year {0}{reason}.", unexpected, Subject?.Year, because, becauseArgs);

    /// <summary>Asserts the subject's month does not equal <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateOnlyAssertions> NotHaveMonth(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have month {0}{reason}.", unexpected, Subject?.Month, because, becauseArgs);

    /// <summary>Asserts the subject's day does not equal <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateOnlyAssertions> NotHaveDay(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have day {0}{reason}.", unexpected, Subject?.Day, because, becauseArgs);

    /// <summary>Asserts the nullable subject has no value. Reads better than <c>NotHaveValue()</c> and is what a reader coming from any other assertion library will reach for first.</summary>
    public AndConstraint<DateOnlyAssertions> BeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be <null>{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has a value, and exposes it via <c>Which</c> so the unwrapped value chains.</summary>
    public AndWhichConstraint<DateOnlyAssertions, DateOnly> NotBeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be <null>{reason}.");
        // GetValueOrDefault, not Value: inside an AssertionScope the failure above is collected
        // rather than thrown, so execution reaches here with no value and .Value would throw an
        // InvalidOperationException that masks the real, already-recorded failure.
        return new(this, Subject.GetValueOrDefault());
    }

    // No null stage: without a value there is no component to object to, so a null subject passes.
    private AndConstraint<DateOnlyAssertions> NotHaveComponent(string matchTemplate, int unexpected, int? actual, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue || actual != unexpected).BecauseOf(because, becauseArgs)
            .FailWith(matchTemplate, unexpected);
        return new(this);
    }
}
