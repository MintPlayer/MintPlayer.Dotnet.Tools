using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on <see cref="TimeSpan"/> (and <see cref="Nullable{TimeSpan}"/>) subjects:
/// equality, sign, proximity, ordering and null checks.
/// Positive assertions fail on a null subject; negative ones treat null as passing.
/// </summary>
public class TimeSpanAssertions
{
    public TimeSpanAssertions(TimeSpan? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test.</summary>
    public TimeSpan? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the subject equals <paramref name="expected"/>.</summary>
    public AndConstraint<TimeSpanAssertions> Be(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not equal <paramref name="unexpected"/>.</summary>
    public AndConstraint<TimeSpanAssertions> NotBe(TimeSpan unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the subject is greater than <see cref="TimeSpan.Zero"/>.</summary>
    public AndConstraint<TimeSpanAssertions> BePositive(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject > TimeSpan.Zero).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be positive{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not greater than <see cref="TimeSpan.Zero"/> — zero and negative spans pass, and so does null.</summary>
    public AndConstraint<TimeSpanAssertions> NotBePositive(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || value <= TimeSpan.Zero).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be positive{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is less than <see cref="TimeSpan.Zero"/>.</summary>
    public AndConstraint<TimeSpanAssertions> BeNegative(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject < TimeSpan.Zero).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be negative{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not less than <see cref="TimeSpan.Zero"/> — zero and positive spans pass, and so does null.</summary>
    public AndConstraint<TimeSpanAssertions> NotBeNegative(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || value >= TimeSpan.Zero).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be negative{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is within <paramref name="precision"/> of <paramref name="nearbyTime"/>.</summary>
    public AndConstraint<TimeSpanAssertions> BeCloseTo(TimeSpan nearbyTime, TimeSpan precision, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, TimeSpan.Zero);
        Assert().ForCondition(Subject.HasValue && Distance(Subject.Value, nearbyTime) <= precision).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be within {0} of {1}{reason}, but found {2}.", precision, nearbyTime, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is not within <paramref name="precision"/> of <paramref name="distantTime"/> (a null subject passes).</summary>
    public AndConstraint<TimeSpanAssertions> NotBeCloseTo(TimeSpan distantTime, TimeSpan precision, string? because = null, params object?[] becauseArgs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, TimeSpan.Zero);
        Assert().ForCondition(!Subject.HasValue || Distance(Subject.Value, distantTime) > precision).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be within {0} of {1}{reason}, but found {2}.", precision, distantTime, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is strictly less than <paramref name="expected"/>.</summary>
    public AndConstraint<TimeSpanAssertions> BeLessThan(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject < expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be less than {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is less than or equal to <paramref name="expected"/>.</summary>
    public AndConstraint<TimeSpanAssertions> BeLessThanOrEqualTo(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject <= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be less than or equal to {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is strictly greater than <paramref name="expected"/>.</summary>
    public AndConstraint<TimeSpanAssertions> BeGreaterThan(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be greater than {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is greater than or equal to <paramref name="expected"/>.</summary>
    public AndConstraint<TimeSpanAssertions> BeGreaterThanOrEqualTo(TimeSpan expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be greater than or equal to {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has a value.</summary>
    public AndConstraint<TimeSpanAssertions> HaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a value{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the nullable subject has no value.</summary>
    public AndConstraint<TimeSpanAssertions> NotHaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a value{reason}, but found {0}.", Subject);
        return new(this);
    }

    private static TimeSpan Distance(TimeSpan left, TimeSpan right)
        => TimeSpan.FromTicks(Math.Abs(left.Ticks - right.Ticks));
}
