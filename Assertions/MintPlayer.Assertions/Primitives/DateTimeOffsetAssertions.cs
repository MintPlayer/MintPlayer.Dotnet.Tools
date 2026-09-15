using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on <see cref="DateTimeOffset"/> (and <see cref="Nullable{DateTimeOffset}"/>) subjects:
/// equality, proximity, ordering, calendar/clock components, offset and null checks.
/// Positive assertions fail on a null subject; negative ones treat null as passing.
/// </summary>
public class DateTimeOffsetAssertions
{
    public DateTimeOffsetAssertions(DateTimeOffset? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test.</summary>
    public DateTimeOffset? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the subject equals <paramref name="expected"/> (same point in time; offsets may differ).</summary>
    public AndConstraint<DateTimeOffsetAssertions> Be(DateTimeOffset expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not equal <paramref name="unexpected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotBe(DateTimeOffset unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the subject is within <paramref name="precision"/> of <paramref name="nearbyTime"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> BeCloseTo(DateTimeOffset nearbyTime, TimeSpan precision, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, TimeSpan.Zero);
        Assert().ForCondition(Subject.HasValue && Distance(Subject.Value, nearbyTime) <= precision).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be within {0} of {1}{reason}, but found {2}.", precision, nearbyTime, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not within <paramref name="precision"/> of <paramref name="distantTime"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotBeCloseTo(DateTimeOffset distantTime, TimeSpan precision, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, TimeSpan.Zero);
        Assert().ForCondition(!Subject.HasValue || Distance(Subject.Value, distantTime) > precision).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be within {0} of {1}{reason}, but found {2}.", precision, distantTime, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is strictly before <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> BeBefore(DateTimeOffset expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject < expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be before {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is on or before <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> BeOnOrBefore(DateTimeOffset expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject <= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be on or before {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is strictly after <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> BeAfter(DateTimeOffset expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be after {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is on or after <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> BeOnOrAfter(DateTimeOffset expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be on or after {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject's year equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> HaveYear(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have year {0}{reason}, but found <null>.", "Expected {subject} to have year {0}{reason}, but found {1}.", expected, Subject?.Year, because, becauseArgs);

    /// <summary>Asserts the subject's month equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> HaveMonth(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have month {0}{reason}, but found <null>.", "Expected {subject} to have month {0}{reason}, but found {1}.", expected, Subject?.Month, because, becauseArgs);

    /// <summary>Asserts the subject's day equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> HaveDay(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have day {0}{reason}, but found <null>.", "Expected {subject} to have day {0}{reason}, but found {1}.", expected, Subject?.Day, because, becauseArgs);

    /// <summary>Asserts the subject's hour equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> HaveHour(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have hour {0}{reason}, but found <null>.", "Expected {subject} to have hour {0}{reason}, but found {1}.", expected, Subject?.Hour, because, becauseArgs);

    /// <summary>Asserts the subject's minute equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> HaveMinute(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have minute {0}{reason}, but found <null>.", "Expected {subject} to have minute {0}{reason}, but found {1}.", expected, Subject?.Minute, because, becauseArgs);

    /// <summary>Asserts the subject's second equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> HaveSecond(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have second {0}{reason}, but found <null>.", "Expected {subject} to have second {0}{reason}, but found {1}.", expected, Subject?.Second, because, becauseArgs);

    /// <summary>Asserts the subject's year differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotHaveYear(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have year {0}{reason}.", unexpected, Subject?.Year, because, becauseArgs);

    /// <summary>Asserts the subject's month differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotHaveMonth(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have month {0}{reason}.", unexpected, Subject?.Month, because, becauseArgs);

    /// <summary>Asserts the subject's day differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotHaveDay(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have day {0}{reason}.", unexpected, Subject?.Day, because, becauseArgs);

    /// <summary>Asserts the subject's hour differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotHaveHour(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have hour {0}{reason}.", unexpected, Subject?.Hour, because, becauseArgs);

    /// <summary>Asserts the subject's minute differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotHaveMinute(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have minute {0}{reason}.", unexpected, Subject?.Minute, because, becauseArgs);

    /// <summary>Asserts the subject's second differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotHaveSecond(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have second {0}{reason}.", unexpected, Subject?.Second, because, becauseArgs);

    /// <summary>Asserts the subject's date component equals that of <paramref name="expected"/> (time of day and offset are ignored).</summary>
    public AndConstraint<DateTimeOffsetAssertions> BeSameDateAs(DateTimeOffset expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue && Subject.Value.Date == expected.Date).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be on {0}{reason}, but found {1}.", expected.Date, Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the subject falls on a different calendar day than <paramref name="unexpected"/>.
    /// Both sides are compared as their own local date, offsets untranslated — the same instant
    /// rendered in two zones can therefore sit on two different dates. A null subject passes.
    /// </summary>
    public AndConstraint<DateTimeOffsetAssertions> NotBeSameDateAs(DateTimeOffset unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue || Subject.Value.Date != unexpected.Date).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on {0}{reason}, but found {1}.", unexpected.Date, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject's offset from UTC equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> HaveOffset(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have offset {0}{reason}, but found <null>.", expected)
            .ForCondition(!Subject.HasValue || Subject.Value.Offset == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have offset {0}{reason}, but found {1}.", expected, Subject?.Offset);
        return new(this);
    }

    /// <summary>Asserts the subject's offset from UTC differs from <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotHaveOffset(TimeSpan unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue || Subject.Value.Offset != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have offset {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has a value.</summary>
    public AndConstraint<DateTimeOffsetAssertions> HaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a value{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the nullable subject has no value.</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotHaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a value{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is one of <paramref name="validValues"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> BeOneOf(params DateTimeOffset[] validValues)
        => BeOneOf(validValues, because: null);

    /// <summary>Asserts the subject is one of <paramref name="validValues"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> BeOneOf(DateTimeOffset[] validValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(validValues);
        Assert().ForCondition(Subject.HasValue && Array.IndexOf(validValues, Subject.Value) >= 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be one of {0}{reason}, but found {1}.", validValues, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is none of <paramref name="unexpectedValues"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotBeOneOf(params DateTimeOffset[] unexpectedValues)
        => NotBeOneOf(unexpectedValues, because: null);

    /// <summary>
    /// Asserts the subject is none of <paramref name="unexpectedValues"/> (a null subject passes).
    /// An empty set passes too: there is nothing for the subject to be one of.
    /// </summary>
    public AndConstraint<DateTimeOffsetAssertions> NotBeOneOf(DateTimeOffset[] unexpectedValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpectedValues);
        Assert().ForCondition(!Subject.HasValue || Array.IndexOf(unexpectedValues, Subject.Value) < 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be one of {0}{reason}, but found {1}.", unexpectedValues, Subject);
        return new(this);
    }

    private static TimeSpan Distance(DateTimeOffset left, DateTimeOffset right)
        => TimeSpan.FromTicks(Math.Abs(left.UtcTicks - right.UtcTicks));

    /// <remarks>
    /// The message templates arrive as literals instead of being built here from a component name.
    /// Concatenating the name into the template ran on EVERY call, passing or failing, putting two
    /// string allocations on the passing path of every component assertion - exactly what the
    /// Phase 2 boundary forbids. Literals cost nothing, and the arguments are only boxed into an
    /// array once a condition has actually failed.
    /// </remarks>
    private AndConstraint<DateTimeOffsetAssertions> HaveComponent(string nullTemplate, string mismatchTemplate, int expected, int? actual, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith(nullTemplate, expected)
            .ForCondition(!Subject.HasValue || actual == expected).BecauseOf(because, becauseArgs)
            .FailWith(mismatchTemplate, expected, actual);
        return new(this);
    }

    // The negative needs no null stage: without a value there is no component to object to, so null passes.
    private AndConstraint<DateTimeOffsetAssertions> NotHaveComponent(string matchTemplate, int unexpected, int? actual, string? because, object?[] becauseArgs)
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
    public AndConstraint<DateTimeOffsetAssertions> NotBeBefore(DateTimeOffset unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject < unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be before {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not on or before <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotBeOnOrBefore(DateTimeOffset unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject <= unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on or before {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not after <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotBeAfter(DateTimeOffset unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject > unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be after {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not on or after <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotBeOnOrAfter(DateTimeOffset unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject >= unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on or after {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject's millisecond equals <paramref name="expected"/>.</summary>
    public AndConstraint<DateTimeOffsetAssertions> HaveMillisecond(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have millisecond {0}{reason}, but found <null>.", "Expected {subject} to have millisecond {0}{reason}, but found {1}.", expected, Subject?.Millisecond, because, becauseArgs);

    /// <summary>Asserts the subject's millisecond does not equal <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotHaveMillisecond(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have millisecond {0}{reason}.", unexpected, Subject?.Millisecond, because, becauseArgs);

    /// <summary>
    /// Asserts the subject equals <paramref name="expected"/> <b>including its offset</b>.
    /// </summary>
    /// <remarks>
    /// <see cref="Be"/> compares instants, so 2024-01-01T12:00+00:00 and 2024-01-01T13:00+01:00 are
    /// equal to it - they are the same moment. <c>BeExactly</c> additionally requires the same
    /// <see cref="DateTimeOffset.Offset"/>, which is what a test round-tripping a value through a
    /// wire format or a database actually wants to pin. There was previously no way to express this.
    /// </remarks>
    public AndConstraint<DateTimeOffsetAssertions> BeExactly(DateTimeOffset expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue && Subject.Value.EqualsExact(expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be exactly {0} (same offset){reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not equal <paramref name="unexpected"/> including its offset (a null subject passes).</summary>
    public AndConstraint<DateTimeOffsetAssertions> NotBeExactly(DateTimeOffset unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue || !Subject.Value.EqualsExact(unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be exactly {0} (same offset){reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has no value. Reads better than <c>NotHaveValue()</c> and is what a reader coming from any other assertion library will reach for first.</summary>
    public AndConstraint<DateTimeOffsetAssertions> BeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be <null>{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has a value, and exposes it via <c>Which</c> so the unwrapped value chains.</summary>
    public AndWhichConstraint<DateTimeOffsetAssertions, DateTimeOffset> NotBeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be <null>{reason}.");
        // GetValueOrDefault, not Value: inside an AssertionScope the failure above is collected
        // rather than thrown, so execution reaches here with no value and .Value would throw an
        // InvalidOperationException that masks the real, already-recorded failure.
        return new(this, Subject.GetValueOrDefault());
    }
}
