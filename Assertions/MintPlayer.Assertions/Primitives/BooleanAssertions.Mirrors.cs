namespace MintPlayer.Assertions.Primitives;

public partial class BooleanAssertions
{
    /// <summary>Asserts the value is not <see langword="true"/>.</summary>
    /// <remarks>
    /// ⚠️ <b>Not the same as <c>BeFalse()</c>, because the subject may be null.</b> A
    /// <c>bool?</c> that is null is not true, so this passes — while <c>BeFalse()</c> fails on it.
    /// Three states, three answers; treating the pair as opposites is only safe for a plain
    /// <c>bool</c>, and the type system will not tell you which one you have at the call site.
    /// </remarks>
    public AndConstraint<BooleanAssertions> NotBeTrue(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != true).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be true{reason}.");
        return new(this);
    }

    /// <summary>Asserts the value is not <see langword="false"/>.</summary>
    /// <remarks>See <see cref="NotBeTrue"/>: a null subject is not false either, so this passes on it.</remarks>
    public AndConstraint<BooleanAssertions> NotBeFalse(string? because = null, params object?[] becauseArgs)
    {
        Assert().ForCondition(Subject != false).BecauseOf(because, becauseArgs)
            .FailWith("Did not expect {subject} to be false{reason}.");
        return new(this);
    }
}
