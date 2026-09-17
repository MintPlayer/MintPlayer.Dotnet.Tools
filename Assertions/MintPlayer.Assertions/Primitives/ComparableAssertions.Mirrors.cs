namespace MintPlayer.Assertions.Primitives;

public partial class ComparableAssertions<T>
{
    /// <summary>Asserts the value is not greater than <paramref name="unexpected"/>.</summary>
    /// <remarks>
    /// ⚠️ <b>Not an alias of <c>BeLessThanOrEqualTo</c>: the null subject behaves differently.</b>
    /// The positive form fails on a null subject, because a value that does not exist is not less
    /// than anything; the negative form passes, because it is not greater than anything either.
    /// Writing one as a call to the other silently flips that for every nullable subject.
    /// </remarks>
    public AndConstraint<ComparableAssertions<T>> NotBeGreaterThan(T unexpected, string? because = null, params object?[] becauseArgs)
        => NotCompare(unexpected, "greater than", c => c <= 0, because, becauseArgs);

    /// <summary>Asserts the value is not greater than or equal to <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeGreaterThan"/> for why these are not aliases.</remarks>
    public AndConstraint<ComparableAssertions<T>> NotBeGreaterThanOrEqualTo(T unexpected, string? because = null, params object?[] becauseArgs)
        => NotCompare(unexpected, "greater than or equal to", c => c < 0, because, becauseArgs);

    /// <summary>Asserts the value is not less than <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeGreaterThan"/> for why these are not aliases.</remarks>
    public AndConstraint<ComparableAssertions<T>> NotBeLessThan(T unexpected, string? because = null, params object?[] becauseArgs)
        => NotCompare(unexpected, "less than", c => c >= 0, because, becauseArgs);

    /// <summary>Asserts the value is not less than or equal to <paramref name="unexpected"/>.</summary>
    /// <remarks>See <see cref="NotBeGreaterThan"/> for why these are not aliases.</remarks>
    public AndConstraint<ComparableAssertions<T>> NotBeLessThanOrEqualTo(T unexpected, string? because = null, params object?[] becauseArgs)
        => NotCompare(unexpected, "less than or equal to", c => c > 0, because, becauseArgs);

    /// <summary>
    /// Shared shape: a null subject passes every negative comparison, and the message is a constant
    /// template so a passing assertion builds nothing.
    /// </summary>
    /// <remarks>
    /// The <c>Func&lt;int, bool&gt;</c> is a non-capturing lambda at each call site, so the compiler
    /// caches it as a static — no display class, no allocation per call. Capturing anything here
    /// would put one on the passing path (PRD §9.15).
    /// </remarks>
    private AndConstraint<ComparableAssertions<T>> NotCompare(
        T unexpected, string relation, Func<int, bool> accept, string? because, object?[] becauseArgs)
    {
        var ok = !hasValue || accept(Subject!.CompareTo(unexpected));
        if (!ok)
        {
            Assert().ForCondition(false).BecauseOf(because, becauseArgs)
                .FailWith($"Did not expect {{subject}} to be {relation} {{0}}{{reason}}, but found {{1}}.", unexpected, SubjectForMessage);
        }
        return new(this);
    }
}
