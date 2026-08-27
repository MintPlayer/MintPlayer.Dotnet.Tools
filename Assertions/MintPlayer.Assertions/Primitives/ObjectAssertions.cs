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
}
