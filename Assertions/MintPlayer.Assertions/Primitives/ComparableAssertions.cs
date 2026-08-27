using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on any <see cref="IComparable{T}"/> subject — reference or value type — using
/// <see cref="IComparable{T}.CompareTo"/> semantics: equality means CompareTo returns 0.
/// </summary>
public class ComparableAssertions<T>
    where T : IComparable<T>
{
    // T is unconstrained struct-vs-class, so a null subject is tracked explicitly:
    // for value types default(T) is a legitimate value and cannot double as "no value".
    private readonly bool hasValue;

    public ComparableAssertions(IComparable<T>? subject, string? subjectExpression)
    {
        if (subject is T value)
        {
            Subject = value;
            hasValue = true;
        }
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test (default when the subject was null).</summary>
    public T? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the value compares equal to the expected value (CompareTo returns 0).</summary>
    public AndConstraint<ComparableAssertions<T>> Be(T expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(hasValue && Subject!.CompareTo(expected) == 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, SubjectForMessage);
        return new(this);
    }

    /// <summary>Asserts the value does not compare equal to the unexpected value (a null value passes).</summary>
    public AndConstraint<ComparableAssertions<T>> NotBe(T unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!hasValue || Subject!.CompareTo(unexpected) != 0).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the value compares less than the expected value.</summary>
    public AndConstraint<ComparableAssertions<T>> BeLessThan(T expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(hasValue && Subject!.CompareTo(expected) < 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be less than {0}{reason}, but found {1}.", expected, SubjectForMessage);
        return new(this);
    }

    /// <summary>Asserts the value compares less than or equal to the expected value.</summary>
    public AndConstraint<ComparableAssertions<T>> BeLessThanOrEqualTo(T expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(hasValue && Subject!.CompareTo(expected) <= 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be less than or equal to {0}{reason}, but found {1}.", expected, SubjectForMessage);
        return new(this);
    }

    /// <summary>Asserts the value compares greater than the expected value.</summary>
    public AndConstraint<ComparableAssertions<T>> BeGreaterThan(T expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(hasValue && Subject!.CompareTo(expected) > 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be greater than {0}{reason}, but found {1}.", expected, SubjectForMessage);
        return new(this);
    }

    /// <summary>Asserts the value compares greater than or equal to the expected value.</summary>
    public AndConstraint<ComparableAssertions<T>> BeGreaterThanOrEqualTo(T expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(hasValue && Subject!.CompareTo(expected) >= 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be greater than or equal to {0}{reason}, but found {1}.", expected, SubjectForMessage);
        return new(this);
    }

    /// <summary>Asserts the value lies within the inclusive range [<paramref name="minimumValue"/>, <paramref name="maximumValue"/>].</summary>
    public AndConstraint<ComparableAssertions<T>> BeInRange(T minimumValue, T maximumValue, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(hasValue && Subject!.CompareTo(minimumValue) >= 0 && Subject!.CompareTo(maximumValue) <= 0).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be between {0} and {1}{reason}, but found {2}.", minimumValue, maximumValue, SubjectForMessage);
        return new(this);
    }

    // Renders default(T) of a null reference subject as <null> instead of a misleading default value.
    private object? SubjectForMessage => hasValue ? Subject : null;
}
