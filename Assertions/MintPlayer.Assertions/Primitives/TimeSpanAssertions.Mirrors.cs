namespace MintPlayer.Assertions.Primitives;

public partial class TimeSpanAssertions
{
    /// <summary>Asserts the value is not greater than <paramref name="unexpected"/>.</summary>
    /// <remarks>
    /// ⚠️ <b>Not an alias of <c>BeLessThanOrEqualTo</c>, and the difference is the null subject.</b>
    /// The positive form fails on null — a value that does not exist is not less than anything. The
    /// negative form passes — a value that does not exist is not greater than anything either. That
    /// is the documented rule for this whole file ("positive assertions fail on a null subject;
    /// negative ones treat null as passing"), and writing one of these as a call to its counterpart
    /// silently flips it for every nullable subject in a suite.
    /// </remarks>
    public AndConstraint<TimeSpanAssertions> NotBeGreaterThan(TimeSpan unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null || Subject <= unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be greater than {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is not greater than or equal to <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeGreaterThan"/> for why these are not aliases.</remarks>
    public AndConstraint<TimeSpanAssertions> NotBeGreaterThanOrEqualTo(TimeSpan unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null || Subject < unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be greater than or equal to {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is not less than <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeGreaterThan"/> for why these are not aliases.</remarks>
    public AndConstraint<TimeSpanAssertions> NotBeLessThan(TimeSpan unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null || Subject >= unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be less than {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is not less than or equal to <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeGreaterThan"/> for why these are not aliases.</remarks>
    public AndConstraint<TimeSpanAssertions> NotBeLessThanOrEqualTo(TimeSpan unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is null || Subject > unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be less than or equal to {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }
}
