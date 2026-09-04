using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on <see cref="DateTime"/> (and <see cref="Nullable{DateTime}"/>) subjects:
/// equality, proximity, ordering, calendar/clock components, kind and null checks.
/// Positive assertions fail on a null subject; negative ones treat null as passing.
/// </summary>
public class DateTimeAssertions
{
    public DateTimeAssertions(DateTime? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test.</summary>
    public DateTime? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the subject equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> Be(DateTime expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not equal <paramref name="unexpected"/>.</summary>
    public AndConstraint<DateTimeAssertions> NotBe(DateTime unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the subject is within <paramref name="precision"/> of <paramref name="nearbyTime"/>.</summary>
    public AndConstraint<DateTimeAssertions> BeCloseTo(DateTime nearbyTime, TimeSpan precision, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, TimeSpan.Zero);
        Assert().ForCondition(Subject.HasValue && Distance(Subject.Value, nearbyTime) <= precision).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be within {0} of {1}{reason}, but found {2}.", precision, nearbyTime, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not within <paramref name="precision"/> of <paramref name="distantTime"/>.</summary>
    public AndConstraint<DateTimeAssertions> NotBeCloseTo(DateTime distantTime, TimeSpan precision, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, TimeSpan.Zero);
        Assert().ForCondition(!Subject.HasValue || Distance(Subject.Value, distantTime) > precision).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be within {0} of {1}{reason}, but found {2}.", precision, distantTime, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is strictly before <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> BeBefore(DateTime expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject < expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be before {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is on or before <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> BeOnOrBefore(DateTime expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject <= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be on or before {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is strictly after <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> BeAfter(DateTime expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be after {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is on or after <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> BeOnOrAfter(DateTime expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be on or after {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject's year equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveYear(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("year", expected, Subject?.Year, because, becauseArgs);

    /// <summary>Asserts the subject's month equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveMonth(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("month", expected, Subject?.Month, because, becauseArgs);

    /// <summary>Asserts the subject's day equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveDay(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("day", expected, Subject?.Day, because, becauseArgs);

    /// <summary>Asserts the subject's hour equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveHour(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("hour", expected, Subject?.Hour, because, becauseArgs);

    /// <summary>Asserts the subject's minute equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveMinute(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("minute", expected, Subject?.Minute, because, becauseArgs);

    /// <summary>Asserts the subject's second equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveSecond(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("second", expected, Subject?.Second, because, becauseArgs);

    /// <summary>Asserts the subject's year differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveYear(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("year", unexpected, Subject?.Year, because, becauseArgs);

    /// <summary>Asserts the subject's month differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveMonth(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("month", unexpected, Subject?.Month, because, becauseArgs);

    /// <summary>Asserts the subject's day differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveDay(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("day", unexpected, Subject?.Day, because, becauseArgs);

    /// <summary>Asserts the subject's hour differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveHour(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("hour", unexpected, Subject?.Hour, because, becauseArgs);

    /// <summary>Asserts the subject's minute differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveMinute(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("minute", unexpected, Subject?.Minute, because, becauseArgs);

    /// <summary>Asserts the subject's second differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveSecond(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("second", unexpected, Subject?.Second, because, becauseArgs);

    /// <summary>Asserts the subject's date component equals that of <paramref name="expected"/> (time of day is ignored).</summary>
    public AndConstraint<DateTimeAssertions> BeSameDateAs(DateTime expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue && Subject.Value.Date == expected.Date).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be on {0}{reason}, but found {1}.", expected.Date, Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the subject falls on a different calendar day than <paramref name="unexpected"/>
    /// (time of day is ignored on both sides, so a different clock time on the same day still fails).
    /// A null subject passes.
    /// </summary>
    public AndConstraint<DateTimeAssertions> NotBeSameDateAs(DateTime unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue || Subject.Value.Date != unexpected.Date).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on {0}{reason}, but found {1}.", unexpected.Date, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject's <see cref="DateTime.Kind"/> equals <paramref name="expectedKind"/>.</summary>
    public AndConstraint<DateTimeAssertions> BeIn(DateTimeKind expectedKind, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be in {0}{reason}, but found <null>.", expectedKind)
            .ForCondition(!Subject.HasValue || Subject.Value.Kind == expectedKind).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be in {0}{reason}, but found {1}.", expectedKind, Subject?.Kind);
        return new(this);
    }

    /// <summary>
    /// Asserts the subject's <see cref="DateTime.Kind"/> differs from <paramref name="unexpectedKind"/>.
    /// A null subject passes — it carries no kind to object to. Note that
    /// <see cref="DateTimeKind.Unspecified"/> is a kind like any other here, so an unspecified subject
    /// fails <c>NotBeIn(DateTimeKind.Unspecified)</c>.
    /// </summary>
    public AndConstraint<DateTimeAssertions> NotBeIn(DateTimeKind unexpectedKind, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue || Subject.Value.Kind != unexpectedKind).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be in {0}{reason}.", unexpectedKind);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has a value.</summary>
    public AndConstraint<DateTimeAssertions> HaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a value{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the nullable subject has no value.</summary>
    public AndConstraint<DateTimeAssertions> NotHaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a value{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is one of <paramref name="validValues"/>.</summary>
    public AndConstraint<DateTimeAssertions> BeOneOf(params DateTime[] validValues)
        => BeOneOf(validValues, because: null);

    /// <summary>Asserts the subject is one of <paramref name="validValues"/>.</summary>
    public AndConstraint<DateTimeAssertions> BeOneOf(DateTime[] validValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(validValues);
        Assert().ForCondition(Subject.HasValue && Array.IndexOf(validValues, Subject.Value) >= 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be one of {0}{reason}, but found {1}.", validValues, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is none of <paramref name="unexpectedValues"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotBeOneOf(params DateTime[] unexpectedValues)
        => NotBeOneOf(unexpectedValues, because: null);

    /// <summary>
    /// Asserts the subject is none of <paramref name="unexpectedValues"/> (a null subject passes).
    /// An empty set passes too: there is nothing for the subject to be one of.
    /// </summary>
    public AndConstraint<DateTimeAssertions> NotBeOneOf(DateTime[] unexpectedValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpectedValues);
        Assert().ForCondition(!Subject.HasValue || Array.IndexOf(unexpectedValues, Subject.Value) < 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be one of {0}{reason}, but found {1}.", unexpectedValues, Subject);
        return new(this);
    }

    private static TimeSpan Distance(DateTime left, DateTime right)
        => TimeSpan.FromTicks(Math.Abs(left.Ticks - right.Ticks));

    private AndConstraint<DateTimeAssertions> HaveComponent(string name, int expected, int? actual, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have " + name + " {0}{reason}, but found <null>.", expected)
            .ForCondition(!Subject.HasValue || actual == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have " + name + " {0}{reason}, but found {1}.", expected, actual);
        return new(this);
    }

    // The negative needs no null stage: without a value there is no component to object to, so null passes.
    private AndConstraint<DateTimeAssertions> NotHaveComponent(string name, int unexpected, int? actual, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue || actual != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have " + name + " {0}{reason}.", unexpected);
        return new(this);
    }
}
