using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on enum subjects (and their nullables): equality, flags, definedness and
/// set membership.
/// </summary>
public class EnumAssertions<TEnum>
    where TEnum : struct, Enum
{
    public EnumAssertions(TEnum? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test.</summary>
    public TEnum? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the value equals the expected value (null equals null).</summary>
    public AndConstraint<EnumAssertions<TEnum>> Be(TEnum? expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(EqualityComparer<TEnum?>.Default.Equals(Subject, expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value does not equal the unexpected value.</summary>
    public AndConstraint<EnumAssertions<TEnum>> NotBe(TEnum? unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!EqualityComparer<TEnum?>.Default.Equals(Subject, unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the value has the given flag set.</summary>
    public AndConstraint<EnumAssertions<TEnum>> HaveFlag(TEnum flag, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && value.HasFlag(flag)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have flag {0}{reason}, but found {1}.", flag, Subject);
        return new(this);
    }

    /// <summary>Asserts the value does not have the given flag set (a null value passes).</summary>
    public AndConstraint<EnumAssertions<TEnum>> NotHaveFlag(TEnum flag, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || !value.HasFlag(flag)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have flag {0}{reason}, but found {1}.", flag, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is one of the declared members of <typeparamref name="TEnum"/>.</summary>
    public AndConstraint<EnumAssertions<TEnum>> BeDefined(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is { } value && Enum.IsDefined(value)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be defined in {0}{reason}, but found {1}.", typeof(TEnum), Subject);
        return new(this);
    }

    /// <summary>
    /// Asserts the value is not one of the declared members of <typeparamref name="TEnum"/> — the check
    /// for the out-of-range values a cast from an integer can produce (a null value passes).
    /// </summary>
    public AndConstraint<EnumAssertions<TEnum>> NotBeDefined(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || !Enum.IsDefined(value)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be defined in {0}{reason}, but found {1}.", typeof(TEnum), Subject);
        return new(this);
    }

    /// <summary>Asserts the value is one of the given values.</summary>
    public AndConstraint<EnumAssertions<TEnum>> BeOneOf(params TEnum[] validValues)
        => BeOneOf(validValues, because: null);

    /// <summary>Asserts the value is one of the given values.</summary>
    public AndConstraint<EnumAssertions<TEnum>> BeOneOf(IEnumerable<TEnum> validValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(validValues);
        Assert().ForCondition(Subject is { } value && validValues.Contains(value, EqualityComparer<TEnum>.Default)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be one of {0}{reason}, but found {1}.", validValues, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is none of the given values (a null value passes).</summary>
    public AndConstraint<EnumAssertions<TEnum>> NotBeOneOf(params TEnum[] unexpectedValues)
        => NotBeOneOf(unexpectedValues, because: null);

    /// <summary>
    /// Asserts the value is none of the given values (a null value passes). An empty set passes too:
    /// there is nothing for the value to be one of.
    /// </summary>
    public AndConstraint<EnumAssertions<TEnum>> NotBeOneOf(IEnumerable<TEnum> unexpectedValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpectedValues);
        Assert().ForCondition(Subject is not { } value || !unexpectedValues.Contains(value, EqualityComparer<TEnum>.Default)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be one of {0}{reason}, but found {1}.", unexpectedValues, Subject);
        return new(this);
    }

    /// <summary>Asserts the nullable value is not null.</summary>
    public AndConstraint<EnumAssertions<TEnum>> HaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a value{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the nullable value is null.</summary>
    public AndConstraint<EnumAssertions<TEnum>> NotHaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a value{reason}, but found {0}.", Subject);
        return new(this);
    }
}
