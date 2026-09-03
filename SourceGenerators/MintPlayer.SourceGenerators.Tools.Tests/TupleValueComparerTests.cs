using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// The tuple comparers, which exist so an incremental pipeline carrying a tuple compares its
/// elements rather than falling back to reference equality on a boxed value.
/// </summary>
/// <remarks>
/// These sat at exactly 0% and were, in the phase 2 PRD, wrongly predicted to light up once the
/// generator harness ran a second driver pass (spike S2). They did not: no generator in this repo
/// uses tuple-typed pipeline values, so nothing constructs them. They are still shipped, still
/// public behaviour of the package, and testable directly — which is what this file does.
/// </remarks>
public class TupleValueComparerTests
{
    #region Two elements

    [Fact]
    public void TwoElement_EqualTuplesAreEqual()
    {
        var comparer = new ValueTupleValueComparer<string, int>();

        comparer.Equals(("a", 1), ("a", 1)).Should().BeTrue();
    }

    [Theory]
    [InlineData("b", 1)]
    [InlineData("a", 2)]
    public void TwoElement_ADifferenceInAnyElementMakesThemUnequal(string second, int third)
    {
        var comparer = new ValueTupleValueComparer<string, int>();

        comparer.Equals(("a", 1), (second, third)).Should().BeFalse();
    }

    /// <summary>
    /// Equal values must produce equal hash codes, or a comparer used as a dictionary key silently
    /// misses — the failure mode is a cache that never hits, not an exception.
    /// </summary>
    [Fact]
    public void TwoElement_EqualTuplesShareAHashCode()
    {
        var comparer = new ValueTupleValueComparer<string, int>();

        comparer.GetHashCode(("a", 1)).Should().Be(comparer.GetHashCode(("a", 1)));
    }

    [Fact]
    public void TwoElement_HandlesNullElements()
    {
        var comparer = new ValueTupleValueComparer<string?, string?>();

        comparer.Equals((null, null), (null, null)).Should().BeTrue();
        comparer.Equals((null, "a"), ("a", null)).Should().BeFalse();
    }

    #endregion

    #region Three and four elements

    [Fact]
    public void ThreeElement_ComparesEveryElement()
    {
        var comparer = new ValueTupleValueComparer<string, int, bool>();

        comparer.Equals(("a", 1, true), ("a", 1, true)).Should().BeTrue();
        comparer.Equals(("a", 1, true), ("a", 1, false)).Should().BeFalse();
        comparer.Equals(("a", 1, true), ("a", 2, true)).Should().BeFalse();
        comparer.Equals(("a", 1, true), ("b", 1, true)).Should().BeFalse();
    }

    [Fact]
    public void FourElement_ComparesEveryElement()
    {
        var comparer = new ValueTupleValueComparer<string, int, bool, char>();

        comparer.Equals(("a", 1, true, 'x'), ("a", 1, true, 'x')).Should().BeTrue();
        comparer.Equals(("a", 1, true, 'x'), ("a", 1, true, 'y')).Should().BeFalse();
    }

    [Fact]
    public void ThreeElement_EqualTuplesShareAHashCode()
    {
        var comparer = new ValueTupleValueComparer<string, int, bool>();

        comparer.GetHashCode(("a", 1, true)).Should().Be(comparer.GetHashCode(("a", 1, true)));
    }

    #endregion

    #region Nullable tuples

    [Fact]
    public void Nullable_TwoNullsAreEqual()
    {
        var comparer = new NullableValueTupleValueComparer<string, int>();

        comparer.Equals(null, null).Should().BeTrue();
    }

    [Fact]
    public void Nullable_ANullAndAValueAreNotEqual()
    {
        var comparer = new NullableValueTupleValueComparer<string, int>();

        comparer.Equals(null, ("a", 1)).Should().BeFalse();
        comparer.Equals(("a", 1), null).Should().BeFalse();
    }

    [Fact]
    public void Nullable_TwoEqualValuesAreEqual()
    {
        var comparer = new NullableValueTupleValueComparer<string, int>();

        comparer.Equals(("a", 1), ("a", 1)).Should().BeTrue();
        comparer.Equals(("a", 1), ("a", 2)).Should().BeFalse();
    }

    [Fact]
    public void Nullable_ThreeAndFourElements()
    {
        var three = new NullableValueTupleValueComparer<string, int, bool>();
        three.Equals(("a", 1, true), ("a", 1, true)).Should().BeTrue();
        three.Equals(("a", 1, true), ("a", 1, false)).Should().BeFalse();
        three.Equals(null, ("a", 1, true)).Should().BeFalse();

        var four = new NullableValueTupleValueComparer<string, int, bool, char>();
        four.Equals(("a", 1, true, 'x'), ("a", 1, true, 'x')).Should().BeTrue();
        four.Equals(("a", 1, true, 'x'), ("a", 1, true, 'y')).Should().BeFalse();
        four.Equals(null, null).Should().BeTrue();
    }

    /// <summary>
    /// A null must hash without throwing — the comparer is reached with nulls precisely when an
    /// optional pipeline value has not been produced yet.
    /// </summary>
    [Fact]
    public void Nullable_ANullHashesWithoutThrowing()
    {
        var comparer = new NullableValueTupleValueComparer<string, int>();

        comparer.GetHashCode(null).Should().Be(comparer.GetHashCode(null));
    }

    #endregion
}
