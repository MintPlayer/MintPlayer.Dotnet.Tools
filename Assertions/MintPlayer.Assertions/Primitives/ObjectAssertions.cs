namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on any object: equality (via <see cref="object.Equals(object?)"/>), null checks,
/// reference identity and type checks. Object-graph equivalency (BeEquivalentTo) is provided by
/// extension methods in the Equivalency namespace part of this package.
/// </summary>
public class ObjectAssertions : ReferenceTypeAssertions<object, ObjectAssertions>
{
    public ObjectAssertions(object? subject, string? subjectExpression) : base(subject, subjectExpression) { }

    public AndConstraint<ObjectAssertions> Be(object? expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Equals(Subject, expected)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    public AndConstraint<ObjectAssertions> NotBe(object? unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Equals(Subject, unexpected)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the subject is one of <paramref name="validValues"/>, using <see cref="object.Equals(object?, object?)"/>.</summary>
    public AndConstraint<ObjectAssertions> BeOneOf(params object?[] validValues)
        => BeOneOf(validValues, because: null);

    /// <summary>Asserts the subject is one of <paramref name="validValues"/>, using <see cref="object.Equals(object?, object?)"/>.</summary>
    public AndConstraint<ObjectAssertions> BeOneOf(object?[] validValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(validValues);
        Assert().ForCondition(IsOneOf(validValues)).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be one of {0}{reason}, but found {1}.", validValues, Subject);
        return new(this);
    }

    /// <summary>Asserts the subject is none of <paramref name="unexpectedValues"/>.</summary>
    public AndConstraint<ObjectAssertions> NotBeOneOf(params object?[] unexpectedValues)
        => NotBeOneOf(unexpectedValues, because: null);

    /// <summary>Asserts the subject is none of <paramref name="unexpectedValues"/>. An empty set passes.</summary>
    public AndConstraint<ObjectAssertions> NotBeOneOf(object?[] unexpectedValues, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpectedValues);
        Assert().ForCondition(!IsOneOf(unexpectedValues)).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be one of {0}{reason}, but found {1}.", unexpectedValues, Subject);
        return new(this);
    }

    // foreach with an early exit, not LINQ Contains(): nothing allocated on the passing path.
    private bool IsOneOf(object?[] values)
    {
        foreach (var value in values)
        {
            if (Equals(Subject, value)) return true;
        }

        return false;
    }
}
