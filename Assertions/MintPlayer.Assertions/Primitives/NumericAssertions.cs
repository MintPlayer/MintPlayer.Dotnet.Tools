using System.Numerics;
using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on numeric subjects (and their nullables): equality, sign, comparison, range,
/// set membership and approximate equality. Equality goes through
/// <see cref="EqualityComparer{T}.Default"/>, so <c>double.NaN.Should().Be(double.NaN)</c> passes.
/// </summary>
public class NumericAssertions<T>
    where T : struct, INumber<T>
{
    public NumericAssertions(T? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test.</summary>
    public T? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the value equals the expected value (null equals null; NaN equals NaN).</summary>
    public AndConstraint<NumericAssertions<T>> Be(T? expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(EqualityComparer<T?>.Default.Equals(Subject, expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value does not equal the unexpected value.</summary>
    public AndConstraint<NumericAssertions<T>> NotBe(T? unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!EqualityComparer<T?>.Default.Equals(Subject, unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the value is greater than zero.</summary>
    public AndConstraint<NumericAssertions<T>> BePositive(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && value > T.Zero).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be positive{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the value is less than zero.</summary>
    public AndConstraint<NumericAssertions<T>> BeNegative(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && value < T.Zero).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be negative{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the value is greater than the expected value.</summary>
    public AndConstraint<NumericAssertions<T>> BeGreaterThan(T expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && value > expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be greater than {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is greater than or equal to the expected value.</summary>
    public AndConstraint<NumericAssertions<T>> BeGreaterThanOrEqualTo(T expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && value >= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be greater than or equal to {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is less than the expected value.</summary>
    public AndConstraint<NumericAssertions<T>> BeLessThan(T expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && value < expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be less than {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is less than or equal to the expected value.</summary>
    public AndConstraint<NumericAssertions<T>> BeLessThanOrEqualTo(T expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && value <= expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be less than or equal to {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value lies within the inclusive range [<paramref name="minimumValue"/>, <paramref name="maximumValue"/>].</summary>
    public AndConstraint<NumericAssertions<T>> BeInRange(T minimumValue, T maximumValue, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && value >= minimumValue && value <= maximumValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be between {0} and {1}{reason}, but found {2}.", minimumValue, maximumValue, Subject);
        return new(this);
    }

    /// <summary>Asserts the value lies outside the inclusive range [<paramref name="minimumValue"/>, <paramref name="maximumValue"/>] (a null value passes).</summary>
    public AndConstraint<NumericAssertions<T>> NotBeInRange(T minimumValue, T maximumValue, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || value < minimumValue || value > maximumValue).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be between {0} and {1}{reason}, but found {2}.", minimumValue, maximumValue, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is one of the given values.</summary>
    public AndConstraint<NumericAssertions<T>> BeOneOf(params T[] validValues)
        => BeOneOf(validValues, because: null);

    /// <summary>Asserts the value is one of the given values.</summary>
    public AndConstraint<NumericAssertions<T>> BeOneOf(IEnumerable<T> validValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(validValues);
        Assert().ForCondition(Subject is { } value && validValues.Contains(value, EqualityComparer<T>.Default)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be one of {0}{reason}, but found {1}.", validValues, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is within <paramref name="delta"/> of <paramref name="expected"/> (inclusive).</summary>
    public AndConstraint<NumericAssertions<T>> BeCloseTo(T expected, T delta, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && Difference(value, expected) <= delta).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be within {0} of {1}{reason}, but found {2}.", delta, expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is further than <paramref name="delta"/> from <paramref name="unexpected"/> (a null value passes).</summary>
    public AndConstraint<NumericAssertions<T>> NotBeCloseTo(T unexpected, T delta, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || Difference(value, unexpected) > delta).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be within {0} of {1}{reason}, but found {2}.", delta, unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the nullable value is not null.</summary>
    public AndConstraint<NumericAssertions<T>> HaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a value{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the nullable value is null.</summary>
    public AndConstraint<NumericAssertions<T>> NotHaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a value{reason}, but found {0}.", Subject);
        return new(this);
    }

    // Subtract the smaller from the larger before T.Abs so unsigned types never wrap.
    private static T Difference(T left, T right) => T.Abs(left >= right ? left - right : right - left);
}
