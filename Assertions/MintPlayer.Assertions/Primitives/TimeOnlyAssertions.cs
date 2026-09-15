using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on <see cref="TimeOnly"/> (and <see cref="Nullable{TimeOnly}"/>) subjects:
/// equality, proximity (wrap-around aware), ordering, clock components and null checks.
/// Positive assertions fail on a null subject; negative ones treat null as passing.
/// </summary>
public class TimeOnlyAssertions
{
    public TimeOnlyAssertions(TimeOnly? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test.</summary>
    public TimeOnly? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the subject equals <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> Be(TimeOnly expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not equal <paramref name="unexpected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> NotBe(TimeOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>
    /// Asserts the subject is within <paramref name="precision"/> of <paramref name="nearbyTime"/>,
    /// measuring the shortest distance around the clock (so 23:59 is close to 00:01).
    /// </summary>
    public AndConstraint<TimeOnlyAssertions> BeCloseTo(TimeOnly nearbyTime, TimeSpan precision, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, TimeSpan.Zero);
        Assert().ForCondition(Subject.HasValue && Distance(Subject.Value, nearbyTime) <= precision).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be within {0} of {1}{reason}, but found {2}.", precision, nearbyTime, Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the subject is not within <paramref name="precision"/> of <paramref name="distantTime"/>,
    /// measuring the same shortest distance around the clock as <see cref="BeCloseTo"/> (so 00:01 is not
    /// far from 23:59). A null subject passes.
    /// </summary>
    public AndConstraint<TimeOnlyAssertions> NotBeCloseTo(TimeOnly distantTime, TimeSpan precision, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, TimeSpan.Zero);
        Assert().ForCondition(!Subject.HasValue || Distance(Subject.Value, distantTime) > precision).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be within {0} of {1}{reason}, but found {2}.", precision, distantTime, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is strictly before <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> BeBefore(TimeOnly expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject < expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be before {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is at or before <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> BeOnOrBefore(TimeOnly expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject <= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be on or before {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is strictly after <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> BeAfter(TimeOnly expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be after {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is at or after <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> BeOnOrAfter(TimeOnly expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be on or after {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject's hour component equals <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> HaveHours(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have hours {0}{reason}, but found <null>.", "Expected {subject} to have hours {0}{reason}, but found {1}.", expected, Subject?.Hour, because, becauseArgs);

    /// <summary>Asserts the subject's minute component equals <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> HaveMinutes(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have minutes {0}{reason}, but found <null>.", "Expected {subject} to have minutes {0}{reason}, but found {1}.", expected, Subject?.Minute, because, becauseArgs);

    /// <summary>Asserts the subject's second component equals <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> HaveSeconds(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have seconds {0}{reason}, but found <null>.", "Expected {subject} to have seconds {0}{reason}, but found {1}.", expected, Subject?.Second, because, becauseArgs);

    /// <summary>Asserts the subject's millisecond component equals <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> HaveMilliseconds(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("Expected {subject} to have milliseconds {0}{reason}, but found <null>.", "Expected {subject} to have milliseconds {0}{reason}, but found {1}.", expected, Subject?.Millisecond, because, becauseArgs);

    /// <summary>Asserts the nullable subject has a value.</summary>
    public AndConstraint<TimeOnlyAssertions> HaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a value{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the nullable subject has no value.</summary>
    public AndConstraint<TimeOnlyAssertions> NotHaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a value{reason}, but found {0}.", Subject);
        return new(this);
    }

    private static TimeSpan Distance(TimeOnly left, TimeOnly right)
    {
        // TimeOnly subtraction wraps around midnight and is always non-negative;
        // the shorter of the two directions is the circular distance.
        var forward = left - right;
        var backward = right - left;
        return forward < backward ? forward : backward;
    }

    /// <remarks>
    /// The message templates arrive as literals instead of being built here from a component name.
    /// Concatenating the name into the template ran on EVERY call, passing or failing, putting two
    /// string allocations on the passing path of every component assertion - exactly what the
    /// Phase 2 boundary forbids. Literals cost nothing, and the arguments are only boxed into an
    /// array once a condition has actually failed.
    /// </remarks>
    private AndConstraint<TimeOnlyAssertions> HaveComponent(string nullTemplate, string mismatchTemplate, int expected, int? actual, string? because, object?[] becauseArgs)
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
    public AndConstraint<TimeOnlyAssertions> NotBeBefore(TimeOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject < unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be before {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not on or before <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<TimeOnlyAssertions> NotBeOnOrBefore(TimeOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject <= unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on or before {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not after <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<TimeOnlyAssertions> NotBeAfter(TimeOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject > unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be after {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not on or after <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<TimeOnlyAssertions> NotBeOnOrAfter(TimeOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!(Subject >= unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on or after {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject's hours does not equal <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<TimeOnlyAssertions> NotHaveHours(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have hours {0}{reason}.", unexpected, Subject?.Hour, because, becauseArgs);

    /// <summary>Asserts the subject's minutes does not equal <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<TimeOnlyAssertions> NotHaveMinutes(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have minutes {0}{reason}.", unexpected, Subject?.Minute, because, becauseArgs);

    /// <summary>Asserts the subject's seconds does not equal <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<TimeOnlyAssertions> NotHaveSeconds(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have seconds {0}{reason}.", unexpected, Subject?.Second, because, becauseArgs);

    /// <summary>Asserts the subject's milliseconds does not equal <paramref name="unexpected"/> (a null subject passes).</summary>
    public AndConstraint<TimeOnlyAssertions> NotHaveMilliseconds(int unexpected, string? because = null, params object?[] becauseArgs)
        => NotHaveComponent("Did not expect {subject} to have milliseconds {0}{reason}.", unexpected, Subject?.Millisecond, because, becauseArgs);

    /// <summary>Asserts the subject is one of <paramref name="validValues"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> BeOneOf(params TimeOnly[] validValues)
        => BeOneOf(validValues, because: null);

    /// <summary>Asserts the subject is one of <paramref name="validValues"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> BeOneOf(TimeOnly[] validValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(validValues);
        Assert().ForCondition(Subject.HasValue && Array.IndexOf(validValues, Subject.Value) >= 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be one of {0}{reason}, but found {1}.", validValues, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is none of <paramref name="unexpectedValues"/> (a null subject passes).</summary>
    public AndConstraint<TimeOnlyAssertions> NotBeOneOf(params TimeOnly[] unexpectedValues)
        => NotBeOneOf(unexpectedValues, because: null);

    /// <summary>
    /// Asserts the subject is none of <paramref name="unexpectedValues"/> (a null subject passes).
    /// An empty set passes too: there is nothing for the subject to be one of.
    /// </summary>
    public AndConstraint<TimeOnlyAssertions> NotBeOneOf(TimeOnly[] unexpectedValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpectedValues);
        Assert().ForCondition(!Subject.HasValue || Array.IndexOf(unexpectedValues, Subject.Value) < 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be one of {0}{reason}, but found {1}.", unexpectedValues, Subject);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has no value. Reads better than <c>NotHaveValue()</c> and is what a reader coming from any other assertion library will reach for first.</summary>
    public AndConstraint<TimeOnlyAssertions> BeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be <null>{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has a value, and exposes it via <c>Which</c> so the unwrapped value chains.</summary>
    public AndWhichConstraint<TimeOnlyAssertions, TimeOnly> NotBeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be <null>{reason}.");
        // GetValueOrDefault, not Value: inside an AssertionScope the failure above is collected
        // rather than thrown, so execution reaches here with no value and .Value would throw an
        // InvalidOperationException that masks the real, already-recorded failure.
        return new(this, Subject.GetValueOrDefault());
    }

    // No null stage: without a value there is no component to object to, so a null subject passes.
    private AndConstraint<TimeOnlyAssertions> NotHaveComponent(string matchTemplate, int unexpected, int? actual, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue || actual != unexpected).BecauseOf(because, becauseArgs)
            .FailWith(matchTemplate, unexpected);
        return new(this);
    }
}
