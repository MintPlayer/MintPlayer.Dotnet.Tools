using System.Runtime.CompilerServices;
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

    /// <summary>
    /// Asserts the value is not greater than zero. Zero, negatives and null all pass. The condition is
    /// the exact negation of <see cref="BePositive"/> rather than <c>value &lt;= T.Zero</c>, so NaN — which
    /// compares false against everything — passes here instead of failing both forms.
    /// </summary>
    public AndConstraint<NumericAssertions<T>> NotBePositive(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || !(value > T.Zero)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be positive{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the value is not less than zero. Zero, positives and null all pass; as with
    /// <see cref="NotBePositive"/> the condition is the exact negation of <see cref="BeNegative"/>, so NaN passes.
    /// </summary>
    public AndConstraint<NumericAssertions<T>> NotBeNegative(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || !(value < T.Zero)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be negative{reason}, but found {0}.", Subject);
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

    /// <summary>Asserts the value is none of the given values (a null value passes).</summary>
    public AndConstraint<NumericAssertions<T>> NotBeOneOf(params T[] unexpectedValues)
        => NotBeOneOf(unexpectedValues, because: null);

    /// <summary>
    /// Asserts the value is none of the given values (a null value passes). An empty set passes too:
    /// there is nothing for the value to be one of.
    /// </summary>
    public AndConstraint<NumericAssertions<T>> NotBeOneOf(IEnumerable<T> unexpectedValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpectedValues);
        Assert().ForCondition(Subject is not { } value || !unexpectedValues.Contains(value, EqualityComparer<T>.Default)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be one of {0}{reason}, but found {1}.", unexpectedValues, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is within <paramref name="delta"/> of <paramref name="expected"/> (inclusive).</summary>
    public AndConstraint<NumericAssertions<T>> BeApproximately(T expected, T delta, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && Difference(value, expected) <= delta).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be within {0} of {1}{reason}, but found {2}.", delta, expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is further than <paramref name="delta"/> from <paramref name="unexpected"/> (a null value passes).</summary>
    public AndConstraint<NumericAssertions<T>> NotBeApproximately(T unexpected, T delta, string? because = null, params object?[] becauseArgs)
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

    // BeNull/NotBeNull on a nullable value type. The behaviour already existed as
    // NotHaveValue/HaveValue, but `x.Should().BeNull()` is what every reader reaches for first and
    // it simply did not compile. NotBeNull exposes the unwrapped value via Which so it chains.

    /// <summary>Asserts the nullable subject has no value.</summary>
    public AndConstraint<NumericAssertions<T>> BeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be <null>{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has a value, exposed via <c>Which</c>.</summary>
    public AndWhichConstraint<NumericAssertions<T>, T> NotBeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be <null>{reason}.");
        // GetValueOrDefault, not Value: inside an AssertionScope the failure above is collected
        // rather than thrown, so .Value would throw and mask the real, already-recorded failure.
        return new(this, Subject.GetValueOrDefault());
    }

    /// <summary>
    /// Asserts the subject satisfies an arbitrary predicate; the predicate's source text appears in
    /// the failure message.
    /// </summary>
    /// <remarks>
    /// Takes a plain <see cref="Func{T, TResult}"/>, not FluentAssertions' <c>Expression&lt;Func&lt;...&gt;&gt;</c>.
    /// FA needs the expression tree to print the predicate and pays <c>Expression.Compile()</c> —
    /// runtime IL emit, AOT-hostile, slow on first call — for every assertion. The caller-captured
    /// <paramref name="predicateExpression"/> recovers the same text at compile time for nothing.
    ///
    /// <paramref name="becauseArgs"/> is a plain array rather than <c>params</c> so the captured
    /// expression can stay last.
    /// </remarks>
    public AndConstraint<NumericAssertions<T>> Match(Func<T?, bool> predicate, string? because = null, object?[]? becauseArgs = null,
        [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Assert().ForCondition(predicate(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to match condition ({0}){reason}, but found {1}.", predicateExpression, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject does not satisfy <paramref name="predicate"/>.</summary>
    public AndConstraint<NumericAssertions<T>> NotMatch(Func<T?, bool> predicate, string? because = null, object?[]? becauseArgs = null,
        [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Assert().ForCondition(!predicate(Subject)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to match condition ({0}){reason}, but found {1}.", predicateExpression, Subject);
        return new(this);
    }
}
