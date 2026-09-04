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
        => HaveComponent("hours", expected, Subject?.Hour, because, becauseArgs);

    /// <summary>Asserts the subject's minute component equals <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> HaveMinutes(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("minutes", expected, Subject?.Minute, because, becauseArgs);

    /// <summary>Asserts the subject's second component equals <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> HaveSeconds(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("seconds", expected, Subject?.Second, because, becauseArgs);

    /// <summary>Asserts the subject's millisecond component equals <paramref name="expected"/>.</summary>
    public AndConstraint<TimeOnlyAssertions> HaveMilliseconds(int expected, string? because = null, params object?[] becauseArgs)
        => HaveComponent("milliseconds", expected, Subject?.Millisecond, because, becauseArgs);

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

    private AndConstraint<TimeOnlyAssertions> HaveComponent(string name, int expected, int? actual, string? because, object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have " + name + " {0}{reason}, but found <null>.", expected)
            .ForCondition(!Subject.HasValue || actual == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have " + name + " {0}{reason}, but found {1}.", expected, actual);
        return new(this);
    }
}
