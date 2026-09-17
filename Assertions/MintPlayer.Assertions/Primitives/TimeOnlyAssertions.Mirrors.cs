namespace MintPlayer.Assertions.Primitives;

public partial class TimeOnlyAssertions
{
    /// <summary>Asserts the value is not before <paramref name="unexpected"/>.</summary>
    /// <remarks>
    /// ⚠️ <b>These four are not aliases of their <c>BeOnOrAfter</c>/<c>BeOnOrBefore</c>
    /// counterparts, and the difference is the null subject.</b> A positive assertion fails on null —
    /// a moment that does not exist is not on or after anything. A negative one passes — it is not
    /// before anything either. That asymmetry is the documented rule for these types, and writing one
    /// of these as a call to its counterpart silently flips it for every nullable subject in a suite.
    /// </remarks>
    public AndConstraint<TimeOnlyAssertions> NotBeBefore(TimeOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null || Subject >= unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be before {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is not on or before <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeBefore"/> for why these are not aliases.</remarks>
    public AndConstraint<TimeOnlyAssertions> NotBeOnOrBefore(TimeOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null || Subject > unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on or before {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is not after <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeBefore"/> for why these are not aliases.</remarks>
    public AndConstraint<TimeOnlyAssertions> NotBeAfter(TimeOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null || Subject <= unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be after {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is not on or after <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeBefore"/> for why these are not aliases.</remarks>
    public AndConstraint<TimeOnlyAssertions> NotBeOnOrAfter(TimeOnly unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null || Subject < unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be on or after {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }
}
