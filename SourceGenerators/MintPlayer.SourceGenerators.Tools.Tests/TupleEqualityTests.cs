using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// Tuples in pipeline models and <c>Combine</c> steps, now that the tuple comparers are gone.
/// </summary>
/// <remarks>
/// Ported one to one from <c>TupleValueComparerTests</c> (and the tuple case of <c>ComparerRegistryResolutionTests</c>).
/// A <see cref="ValueTuple"/> already compares item by item through <see cref="EqualityComparer{T}.Default"/>, which
/// is right for every item with value equality: strings, primitives and <c>[AutoValueComparer]</c> models. The
/// generator only steps in when an item is a collection; then it builds a
/// <see cref="ValueEquality.DelegateComparer{T}"/> that inlines the item comparisons, which the last region tests.
/// </remarks>
public class TupleEqualityTests
{
    private sealed class Element(int value) : IEquatable<Element>
    {
        public int Value { get; } = value;
        public bool Equals(Element? other) => other is not null && other.Value == Value;
        public override bool Equals(object? obj) => Equals(obj as Element);
        public override int GetHashCode() => Value;
    }

    private static bool Eq<T>(T x, T y) => EqualityComparer<T>.Default.Equals(x, y);
    private static int Hash<T>(T x) => EqualityComparer<T>.Default.GetHashCode(x!);

    #region Two elements

    [Fact]
    public void TwoElement_EqualTuplesAreEqual()
        => Eq(("a", 1), ("a", 1)).Should().BeTrue();

    [Theory]
    [InlineData("b", 1)]
    [InlineData("a", 2)]
    public void TwoElement_ADifferenceInAnyElementMakesThemUnequal(string second, int third)
        => Eq(("a", 1), (second, third)).Should().BeFalse();

    [Fact]
    public void TwoElement_EqualTuplesShareAHashCode()
        => Hash(("a", 1)).Should().Be(Hash((string.Concat("", "a"), 1)));

    [Fact]
    public void TwoElement_HandlesNullElements()
    {
        Eq<(string?, string?)>((null, null), (null, null)).Should().BeTrue();
        Eq<(string?, string?)>((null, "a"), ("a", null)).Should().BeFalse();
    }

    #endregion

    #region Three and four elements

    [Fact]
    public void ThreeElement_ComparesEveryElement()
    {
        Eq(("a", 1, true), ("a", 1, true)).Should().BeTrue();
        Eq(("a", 1, true), ("a", 1, false)).Should().BeFalse();
        Eq(("a", 1, true), ("a", 2, true)).Should().BeFalse();
        Eq(("a", 1, true), ("b", 1, true)).Should().BeFalse();
    }

    [Fact]
    public void FourElement_ComparesEveryElement()
    {
        Eq(("a", 1, true, 'x'), ("a", 1, true, 'x')).Should().BeTrue();
        Eq(("a", 1, true, 'x'), ("a", 1, true, 'y')).Should().BeFalse();
    }

    [Fact]
    public void ThreeElement_EqualTuplesShareAHashCode()
        => Hash(("a", 1, true)).Should().Be(Hash(("a", 1, true)));

    #endregion

    #region Nullable tuples

    [Fact]
    public void Nullable_TwoNullsAreEqual()
        => Eq<(string, int)?>(null, null).Should().BeTrue();

    [Fact]
    public void Nullable_ANullAndAValueAreNotEqual()
    {
        Eq<(string, int)?>(null, ("a", 1)).Should().BeFalse();
        Eq<(string, int)?>(("a", 1), null).Should().BeFalse();
    }

    [Fact]
    public void Nullable_TwoEqualValuesAreEqual()
    {
        Eq<(string, int)?>(("a", 1), ("a", 1)).Should().BeTrue();
        Eq<(string, int)?>(("a", 1), ("a", 2)).Should().BeFalse();
    }

    [Fact]
    public void Nullable_ThreeAndFourElements()
    {
        Eq<(string, int, bool)?>(("a", 1, true), ("a", 1, true)).Should().BeTrue();
        Eq<(string, int, bool)?>(("a", 1, true), ("a", 1, false)).Should().BeFalse();
        Eq<(string, int, bool)?>(null, ("a", 1, true)).Should().BeFalse();

        Eq<(string, int, bool, char)?>(("a", 1, true, 'x'), ("a", 1, true, 'x')).Should().BeTrue();
        Eq<(string, int, bool, char)?>(("a", 1, true, 'x'), ("a", 1, true, 'y')).Should().BeFalse();
        Eq<(string, int, bool, char)?>(null, null).Should().BeTrue();
    }

    [Fact]
    public void Nullable_ANullHashesWithoutThrowing()
        => EqualityComparer<(string, int)?>.Default.GetHashCode(null!).Should().Be(EqualityComparer<(string, int)?>.Default.GetHashCode(null!));

    #endregion

    #region Structural items (from ComparerRegistryResolutionTests.AValueTuple_ComparesItemByItem_ThroughTheItemsComparers)

    /// <summary>Behaviour to preserve #5: a tuple of models compares item by item, by value.</summary>
    [Fact]
    public void AValueTupleOfModels_ComparesItemByItem()
    {
        var a = (new Element(1), new Element(2));
        var b = (new Element(1), new Element(2));

        Eq(a, b).Should().BeTrue();
        Hash(a).Should().Be(Hash(b));
        Eq(a, (a.Item1, new Element(3))).Should().BeFalse();
    }

    /// <summary>
    /// A tuple holding a collection: the tuple's own equality would compare the list by reference, so generated code
    /// passes a delegate comparer that compares the items with <see cref="ValueEquality"/>.
    /// </summary>
    [Fact]
    public void ATupleOfCollections_ComparesThroughADelegateComparer()
    {
        var comparer = new ValueEquality.DelegateComparer<(string Name, ImmutableArray<Element> Items)>(
            (x, y) => string.Equals(x.Name, y.Name, StringComparison.Ordinal) && ValueEquality.ImmutableArray(x.Items, y.Items),
            x => ValueEquality.Combine(StringComparer.Ordinal.GetHashCode(x.Name), ValueEquality.ImmutableArrayHash(x.Items)));

        var a = ("n", ImmutableArray.Create(new Element(1)));
        var b = ("n", ImmutableArray.Create(new Element(1)));

        Eq(a, b).Should().BeFalse("the tuple's own equality compares the ImmutableArray by reference");
        comparer.Equals(a, b).Should().BeTrue();
        comparer.GetHashCode(a).Should().Be(comparer.GetHashCode(b));
        comparer.Equals(a, ("n", ImmutableArray.Create(new Element(2)))).Should().BeFalse();

        ValueEquality.Array([a], new[] { b }, comparer).Should().BeTrue("the delegate comparer composes as an element comparer");
    }

    #endregion
}
