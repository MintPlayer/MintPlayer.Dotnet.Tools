using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on <see cref="bool"/> and nullable <see cref="bool"/> subjects.
/// </summary>
public class BooleanAssertions
{
    public BooleanAssertions(bool? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test.</summary>
    public bool? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the value is true.</summary>
    public AndConstraint<BooleanAssertions> BeTrue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject == true).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be true{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the value is false.</summary>
    public AndConstraint<BooleanAssertions> BeFalse(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject == false).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be false{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the value equals the expected value (null equals null).</summary>
    public AndConstraint<BooleanAssertions> Be(bool? expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value does not equal the unexpected value.</summary>
    public AndConstraint<BooleanAssertions> NotBe(bool? unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the nullable value is not null.</summary>
    public AndConstraint<BooleanAssertions> HaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a value{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the nullable value is null.</summary>
    public AndConstraint<BooleanAssertions> NotHaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a value{reason}, but found {0}.", Subject);
        return new(this);
    }
}
