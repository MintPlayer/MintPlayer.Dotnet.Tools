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
        => HaveComponent("Expected {subject} to have year {0}{reason}, but found <null>.", "Expected {subject} to have year {0}{reason}, but found {1}.", expected, Subject?.Year, because, becauseArgs);

    /// <summary>Asserts the subject's month equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveMonth(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have month {0}{reason}, but found <null>.", "Expected {subject} to have month {0}{reason}, but found {1}.", expected, Subject?.Month, because, becauseArgs);

    /// <summary>Asserts the subject's day equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveDay(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have day {0}{reason}, but found <null>.", "Expected {subject} to have day {0}{reason}, but found {1}.", expected, Subject?.Day, because, becauseArgs);

    /// <summary>Asserts the subject's hour equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveHour(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have hour {0}{reason}, but found <null>.", "Expected {subject} to have hour {0}{reason}, but found {1}.", expected, Subject?.Hour, because, becauseArgs);

    /// <summary>Asserts the subject's minute equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveMinute(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have minute {0}{reason}, but found <null>.", "Expected {subject} to have minute {0}{reason}, but found {1}.", expected, Subject?.Minute, because, becauseArgs);

    /// <summary>Asserts the subject's second equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveSecond(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have second {0}{reason}, but found <null>.", "Expected {subject} to have second {0}{reason}, but found {1}.", expected, Subject?.Second, because, becauseArgs);

    /// <summary>Asserts the subject's year differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveYear(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have year {0}{reason}.", unexpected, Subject?.Year, because, becauseArgs);

    /// <summary>Asserts the subject's month differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveMonth(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have month {0}{reason}.", unexpected, Subject?.Month, because, becauseArgs);

    /// <summary>Asserts the subject's day differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveDay(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have day {0}{reason}.", unexpected, Subject?.Day, because, becauseArgs);

    /// <summary>Asserts the subject's hour differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveHour(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have hour {0}{reason}.", unexpected, Subject?.Hour, because, becauseArgs);

    /// <summary>Asserts the subject's minute differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveMinute(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have minute {0}{reason}.", unexpected, Subject?.Minute, because, becauseArgs);

    /// <summary>Asserts the subject's second differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveSecond(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have second {0}{reason}.", unexpected, Subject?.Second, because, becauseArgs);

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

    /// <remarks>
    /// The message templates arrive as literals instead of being built here from a component name.
    /// Concatenating the name into the template ran on EVERY call, passing or failing, putting two
    /// string allocations on the passing path of every component assertion - exactly what the
    /// Phase 2 boundary forbids. Literals cost nothing, and the arguments are only boxed into an
    /// array once a condition has actually failed.
    /// </remarks>
    private AndConstraint<DateTimeAssertions> HaveComponent(string nullTemplate, string mismatchTemplate, int expected, int? actual, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith(nullTemplate, expected)
            .ForCondition(!Subject.HasValue || actual == expected).BecauseOf(because, becauseArgs)
            .FailWith(mismatchTemplate, expected, actual);
        return new(this);
    }

    // The negative needs no null stage: without a value there is no component to object to, so null passes.
    private AndConstraint<DateTimeAssertions> NotHaveComponent(string matchTemplate, int unexpected, int? actual, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue || actual != unexpected).BecauseOf(because, becauseArgs)
            .FailWith(matchTemplate, unexpected);
        return new(this);
    }

    // The four negatives below. Each is the logical complement of its positive counterpart, and a
    // null subject PASSES every one of them - consistent with the library's documented rule that a
    // negative assertion is satisfied by the absence of a subject. They exist because
    // NotBeBefore(x) reads as what a test means, where the equivalent BeOnOrAfter(x) makes the
    // reader do the inversion.

    /// <summary>Asserts the subject is not before <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotBeBefore(DateTime unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject < unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be before {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not on or before <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotBeOnOrBefore(DateTime unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject <= unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on or before {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not after <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotBeAfter(DateTime unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject > unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be after {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not on or after <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotBeOnOrAfter(DateTime unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject >= unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on or after {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject's millisecond equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeAssertions> HaveMillisecond(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have millisecond {0}{reason}, but found <null>.", "Expected {subject} to have millisecond {0}{reason}, but found {1}.", expected, Subject?.Millisecond, because, becauseArgs);

    /// <summary>Asserts the subject's millisecond does not equal <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeAssertions> NotHaveMillisecond(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have millisecond {0}{reason}.", unexpected, Subject?.Millisecond, because, becauseArgs);

    /// <summary>Asserts the nullable subject has no value. Reads better than <c>NotHaveValue()</c> and is what a reader coming from any other assertion library will reach for first.</summary>
    public AndConstraint<DateTimeAssertions> BeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be <null>{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has a value, and exposes it via <c>Which</c> so the unwrapped value chains.</summary>
    public AndWhichConstraint<DateTimeAssertions, DateTime> NotBeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be <null>{reason}.");
        // GetValueOrDefault, not Value: inside an AssertionScope the failure above is collected
        // rather than thrown, so execution reaches here with no value and .Value would throw an
        // InvalidOperationException that masks the real, already-recorded failure.
        return new(this, Subject.GetValueOrDefault());
    }
}
