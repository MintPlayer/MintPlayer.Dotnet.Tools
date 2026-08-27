using MintPlayer.Assertions.Execution;

namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// Assertions on <see cref="Guid"/> and nullable <see cref="Guid"/> subjects.
/// </summary>
public class GuidAssertions
{
    public GuidAssertions(Guid? subject, string? subjectExpression)
    {
        Subject = subject;
        SubjectExpression = string.IsNullOrWhiteSpace(subjectExpression) ? "value" : subjectExpression!;
    }

    /// <summary>The value under test.</summary>
    public Guid? Subject { get; }

    /// <summary>The caller's expression text for the subject (from CallerArgumentExpression).</summary>
    public string SubjectExpression { get; }

    /// <summary>Starts a failure chain for this subject. Extension authors build on this.</summary>
    public Assertion Assert() => Assertion.For(SubjectExpression);

    /// <summary>Asserts the value equals the expected GUID.</summary>
    public AndConstraint<GuidAssertions> Be(Guid expected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject == expected).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value equals the GUID represented by <paramref name="expected"/>.</summary>
    public AndConstraint<GuidAssertions> Be(string expected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var parsed = Guid.TryParse(expected, out var expectedGuid);
        Assert().ForCondition(parsed).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but {0} is not a valid GUID.", expected)
            .ForCondition(!parsed || Subject == expectedGuid).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be {0}{reason}, but found {1}.", expectedGuid, Subject);
        return new(this);
    }

    /// <summary>Asserts the value does not equal the unexpected GUID.</summary>
    public AndConstraint<GuidAssertions> NotBe(Guid unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpected);
        return new(this);
    }

    /// <summary>Asserts the value does not equal the GUID represented by <paramref name="unexpected"/>.</summary>
    public AndConstraint<GuidAssertions> NotBe(string unexpected, string? because = null, params object?[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(unexpected);
        var parsed = Guid.TryParse(unexpected, out var unexpectedGuid);
        Assert().ForCondition(parsed).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}, but {0} is not a valid GUID.", unexpected)
            .ForCondition(!parsed || Subject != unexpectedGuid).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be {0}{reason}.", unexpectedGuid);
        return new(this);
    }

    /// <summary>Asserts the value is <see cref="Guid.Empty"/>.</summary>
    public AndConstraint<GuidAssertions> BeEmpty(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject == Guid.Empty).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be empty{reason}, but found {0}.", Subject);
        return new(this);
    }

    /// <summary>Asserts the value is not <see cref="Guid.Empty"/> (a null value passes).</summary>
    public AndConstraint<GuidAssertions> NotBeEmpty(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != Guid.Empty).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be empty{reason}.");
        return new(this);
    }

    /// <summary>Asserts the nullable value is not null.</summary>
    public AndConstraint<GuidAssertions> HaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to have a value{reason}, but found <null>.");
        return new(this);
    }

    /// <summary>Asserts the nullable value is null.</summary>
    public AndConstraint<GuidAssertions> NotHaveValue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(!Subject.HasValue).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to have a value{reason}, but found {0}.", Subject);
        return new(this);
    }
}
