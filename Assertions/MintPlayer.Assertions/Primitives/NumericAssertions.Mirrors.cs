using System.Numerics;

namespace MintPlayer.Assertions.Primitives;

public partial class NumericAssertions<T>
    where T : struct, INumber<T>
{
    /// <summary>Asserts the value is not greater than <paramref name="unexpected"/>.</summary>
    /// <remarks>
    /// ⚠️ These four read as the logical negations they are, and they are <b>not</b> aliases of
    /// <c>BeLessThanOrEqualTo</c> and friends — not because the condition differs, but because the
    /// <b>null</b> case does. <c>BeLessThanOrEqualTo</c> on a null subject fails: a value that does
    /// not exist is not less than anything. <c>NotBeGreaterThan</c> on a null subject <i>passes</i>:
    /// a value that does not exist is not greater than anything either. Writing one as a call to the
    /// other silently flips the behaviour of every nullable subject in a suite, which is the kind of
    /// change that produces a green test run and a wrong answer.
    /// </remarks>
    public AndConstraint<NumericAssertions<T>> NotBeGreaterThan(T unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || value <= unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be greater than {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is not greater than or equal to <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeGreaterThan"/> for why these are not aliases.</remarks>
    public AndConstraint<NumericAssertions<T>> NotBeGreaterThanOrEqualTo(T unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || value < unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be greater than or equal to {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is not less than <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeGreaterThan"/> for why these are not aliases.</remarks>
    public AndConstraint<NumericAssertions<T>> NotBeLessThan(T unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || value >= unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be less than {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }

    /// <summary>Asserts the value is not less than or equal to <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeGreaterThan"/> for why these are not aliases.</remarks>
    public AndConstraint<NumericAssertions<T>> NotBeLessThanOrEqualTo(T unexpected, string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject is not { } value || value > unexpected).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be less than or equal to {0}{reason}, but found {1}.", unexpected, Subject);
        return new(this);
    }
}
