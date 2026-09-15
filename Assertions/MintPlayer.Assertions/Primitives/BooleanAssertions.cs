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

    /// <summary>
    /// Asserts the subject is not <c>true</c> — satisfied by <c>false</c> <b>and by null</b>.
    /// </summary>
    /// <remarks>
    /// Not the same assertion as <c>BeFalse()</c>, which a null subject fails. For a tri-state flag
    /// "definitely not true" and "definitely false" are different claims, and only this pair can
    /// express the former.
    /// </remarks>
    public AndConstraint<BooleanAssertions> NotBeTrue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != true).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be true{reason}.");
        return new(this);
    }

    /// <summary>Asserts the subject is not <c>false</c> — satisfied by <c>true</c> and by null. See <see cref="NotBeTrue"/>.</summary>
    public AndConstraint<BooleanAssertions> NotBeFalse(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != false).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be false{reason}.");
        return new(this);
    }

    /// <summary>
    /// Asserts the subject implies <paramref name="consequent"/>: if the subject is true then the
    /// consequent must be true. A false or null subject implies anything.
    /// </summary>
    public AndConstraint<BooleanAssertions> Imply(bool consequent, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != true || consequent).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to imply {0}{reason}, but it did not.", consequent);
        return new(this);
    }

    // BeNull/NotBeNull on a nullable value type. The behaviour already existed as
    // NotHaveValue/HaveValue, but `x.Should().BeNull()` is what every reader reaches for first and
    // it simply did not compile. NotBeNull exposes the unwrapped value via Which so it chains.

    /// <summary>Asserts the nullable subject has no value.</summary>
    public AndConstraint<BooleanAssertions> BeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be <null>{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the nullable subject has a value, exposed via <c>Which</c>.</summary>
    public AndWhichConstraint<BooleanAssertions, bool> NotBeNull(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} not to be <null>{reason}.");
        // GetValueOrDefault, not Value: inside an AssertionScope the failure above is collected
        // rather than thrown, so .Value would throw and mask the real, already-recorded failure.
        return new(this, Subject.GetValueOrDefault());
    }
}
