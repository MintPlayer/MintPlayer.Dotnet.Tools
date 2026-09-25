using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// <see cref="ValueEquality"/>: the reflection-free helper that generated <c>[GenerateEquality]</c> equality and the
/// hand-written <see cref="IEquatable{T}"/> models call into.
/// </summary>
/// <remarks>
/// Ported case by case from the tests of the deleted comparer runtime (<c>ValueComparerTests</c>,
/// <c>ComparerRegistryResolutionTests</c> and <c>TupleValueComparerTests</c>). Each region names the file it came
/// from. The registry-only cases (registration, overwrite, attribute resolution, the negative cache) have no
/// counterpart: there is no registry anymore, and equality is chosen at generation time.
/// </remarks>
public class ValueEqualityTests
{
    /// <summary>An element with value equality, standing in for a generated model.</summary>
    private sealed class Element(int value) : IEquatable<Element>
    {
        public int Value { get; } = value;
        public bool Equals(Element? other) => other is not null && other.Value == Value;
        public override bool Equals(object? obj) => Equals(obj as Element);
        public override int GetHashCode() => Value;
    }

    /// <summary>An element with only reference equality.</summary>
    private sealed class ReferenceOnly(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class NeverEqual<T> : IEqualityComparer<T>
    {
        public bool Equals(T? x, T? y) => false;
        public int GetHashCode(T obj) => 0;
    }

    #region Null and reference shortcuts (from ValueComparerTests: Equals_* and IsEquals_*Null*)

    [Fact]
    public void BothNull_IsEqual_ForEveryShape()
    {
        ValueEquality.Array<string>(null, null).Should().BeTrue();
        ValueEquality.List<string>(null, null).Should().BeTrue();
        ValueEquality.Sequence<string>(null, null).Should().BeTrue();
        ValueEquality.Dictionary<string, int>((IReadOnlyDictionary<string, int>?)null, null).Should().BeTrue();
        ValueEquality.String.Equals(null, null).Should().BeTrue();
    }

    [Fact]
    public void ExactlyOneNull_IsUnequal_ForEveryShape()
    {
        ValueEquality.Array(["a"], null).Should().BeFalse();
        ValueEquality.Array(null, new[] { "a" }).Should().BeFalse();
        ValueEquality.List(["a"], null).Should().BeFalse();
        ValueEquality.Sequence(null, new[] { "a" }).Should().BeFalse();
        ValueEquality.Dictionary((IReadOnlyDictionary<string, int>)new Dictionary<string, int>(), null).Should().BeFalse();
        ValueEquality.String.Equals("a", null).Should().BeFalse();
        ValueEquality.String.Equals(null, "a").Should().BeFalse();
    }

    /// <summary>The same reference is equal without comparing a single element (behaviour to preserve #1).</summary>
    [Fact]
    public void TheSameReference_IsEqual_WithoutComparingElements()
    {
        string[] array = ["a"];
        List<string> list = ["a"];

        ValueEquality.Array(array, array, new NeverEqual<string>()).Should().BeTrue();
        ValueEquality.List(list, list, new NeverEqual<string>()).Should().BeTrue();
        ValueEquality.Sequence(list, list, new NeverEqual<string>()).Should().BeTrue();
    }

    /// <summary>Replaces AreEqual_DefaultsToTrue_WhenNotOverridden: the comparer built from delegates uses exactly those.</summary>
    [Fact]
    public void DelegateComparer_UsesItsDelegates()
    {
        var comparer = new ValueEquality.DelegateComparer<string>((x, y) => x.Length == y.Length, x => x.Length);

        comparer.Equals("ab", "cd").Should().BeTrue();
        comparer.Equals("ab", "abc").Should().BeFalse();
        comparer.GetHashCode("ab").Should().Be(2);
    }

    #endregion

    #region Primitives and strings (from ValueComparerTests: IsEquals_UsesTheDefaultComparerForPrimitives, IsEquals_ComparesStringsByValue)

    [Fact]
    public void Primitives_CompareThroughTheDefaultComparer()
    {
        ValueEquality.Array([1], new[] { 1 }).Should().BeTrue();
        ValueEquality.Array([1], new[] { 2 }).Should().BeFalse();
    }

    /// <summary>Behaviour to preserve #6: strings compare ordinally, by value, not by reference.</summary>
    [Fact]
    public void Strings_CompareOrdinallyByValue()
    {
        ValueEquality.String.Equals("abc", string.Concat("ab", "c")).Should().BeTrue();
        ValueEquality.String.Equals("abc", "abd").Should().BeFalse();
        ValueEquality.String.Equals("abc", "ABC").Should().BeFalse();
        ValueEquality.String.GetHashCode("abc").Should().Be(ValueEquality.String.GetHashCode(string.Concat("ab", "c")));
    }

    #endregion

    #region ImmutableArray (from ValueComparerTests: IsEquals_ComparesImmutableArrays*, GetHashCode_ForAnImmutableArray*; ComparerRegistryResolutionTests: AnImmutableArray_*, ADefaultImmutableArray_*)

    [Fact]
    public void ImmutableArray_ComparesStructurally()
    {
        ValueEquality.ImmutableArray([1, 2, 3], ImmutableArray.Create(1, 2, 3)).Should().BeTrue();
        ValueEquality.ImmutableArray([1, 2, 3], ImmutableArray.Create(1, 2, 4)).Should().BeFalse();
        ValueEquality.ImmutableArray([1, 2], ImmutableArray.Create(1, 2, 3)).Should().BeFalse();
        ValueEquality.ImmutableArray(ImmutableArray<int>.Empty, ImmutableArray<int>.Empty).Should().BeTrue();
    }

    [Fact]
    public void ImmutableArray_OfStrings_IsOrderSensitive()
    {
        ValueEquality.ImmutableArray(["a", "b"], ImmutableArray.Create("a", "b")).Should().BeTrue();
        ValueEquality.ImmutableArray(["a", "b"], ImmutableArray.Create("b", "a")).Should().BeFalse();
    }

    [Fact]
    public void ImmutableArrayHash_IsStructural()
    {
        ValueEquality.ImmutableArrayHash(ImmutableArray.Create(1, 2, 3)).Should().Be(ValueEquality.ImmutableArrayHash(ImmutableArray.Create(1, 2, 3)));
        ValueEquality.ImmutableArrayHash(ImmutableArray.Create(1, 2, 3)).Should().NotBe(ValueEquality.ImmutableArrayHash(ImmutableArray.Create(3, 2, 1)));
    }

    /// <summary>
    /// Replaces GetHashCode_WithoutTheStaticHelper_IsNotStructural. That test pinned an asymmetry of the old base
    /// class; the helper has no such fallback. ImmutableArray's own hash is by array reference, the helper's is not.
    /// </summary>
    [Fact]
    public void ImmutableArrayHash_IsStructural_UnlikeTheArraysOwnHash()
    {
        var a = ImmutableArray.Create(1, 2, 3);
        var b = ImmutableArray.Create(1, 2, 3);

        a.GetHashCode().Should().NotBe(b.GetHashCode());
        ValueEquality.ImmutableArrayHash(a).Should().Be(ValueEquality.ImmutableArrayHash(b));
    }

    [Fact]
    public void ImmutableArray_ComparesElementWise_ThroughTheElementsEquality()
    {
        var a = ImmutableArray.Create(new Element(1));
        var b = ImmutableArray.Create(new Element(1));

        ValueEquality.ImmutableArray(a, b).Should().BeTrue();
        ValueEquality.ImmutableArrayHash(a).Should().Be(ValueEquality.ImmutableArrayHash(b));
        ValueEquality.ImmutableArray(a, ImmutableArray.Create(new Element(2))).Should().BeFalse();
    }

    /// <summary>Behaviour to preserve #3: default equals default, default never equals empty.</summary>
    [Fact]
    public void ADefaultImmutableArray_EqualsOnlyAnotherDefault_AndHashesWithoutThrowing()
    {
        ValueEquality.ImmutableArray<Element>(default, default).Should().BeTrue();
        ValueEquality.ImmutableArray(default, ImmutableArray<Element>.Empty).Should().BeFalse();
        ValueEquality.ImmutableArray(ImmutableArray<Element>.Empty, default).Should().BeFalse();
        ValueEquality.ImmutableArrayHash<Element>(default).Should().Be(ValueEquality.ImmutableArrayHash<Element>(default));
    }

    #endregion

    #region Lists and arrays (from ValueComparerTests: IsEquals_ComparesLists/Arrays*, GetHashCode_ForAList_IsStructural, ArrayValueComparer_*, ListValueComparer_*; ComparerRegistryResolutionTests: AnArray_*, AList_*)

    [Fact]
    public void List_ComparesStructurally_AndOrderSensitively()
    {
        ValueEquality.List(["a", "b"], new List<string> { "a", "b" }).Should().BeTrue();
        ValueEquality.List(["a", "b"], new List<string> { "b", "a" }).Should().BeFalse();
        ValueEquality.List(["a"], new List<string> { "a", "b" }).Should().BeFalse();
    }

    [Fact]
    public void ListHash_IsStructural_AndOrderSensitive()
    {
        ValueEquality.ListHash(new List<string> { "a", "b" }).Should().Be(ValueEquality.ListHash(new List<string> { "a", "b" }));
        ValueEquality.ListHash(new List<string> { "a", "b" }).Should().NotBe(ValueEquality.ListHash(new List<string> { "b", "a" }));
    }

    [Fact]
    public void Array_ComparesStructurally()
    {
        ValueEquality.Array([1, 2], new[] { 1, 2 }).Should().BeTrue();
        ValueEquality.Array([1, 2], new[] { 2, 1 }).Should().BeFalse();
    }

    [Fact]
    public void ArrayComparer_ComparesElementwise()
    {
        var comparer = ValueEquality.ArrayComparer<string>.Instance;

        comparer.Equals(["a", "b"], ["a", "b"]).Should().BeTrue();
        comparer.Equals(["a", "b"], ["a", "c"]).Should().BeFalse();
        comparer.Equals(["a"], ["a", "b"]).Should().BeFalse();
        comparer.Equals([], []).Should().BeTrue();
    }

    [Fact]
    public void ListComparer_ComparesElementwise()
    {
        var comparer = ValueEquality.ListComparer<int>.Instance;

        comparer.Equals(new List<int> { 1, 2 }, new List<int> { 1, 2 }).Should().BeTrue();
        comparer.Equals(new List<int> { 1, 2 }, new List<int> { 1, 3 }).Should().BeFalse();
        comparer.Equals(new List<int>(), new List<int>()).Should().BeTrue();
    }

    [Fact]
    public void AnArray_ComparesElementWise_ThroughTheElementsEquality()
    {
        Element[] a = [new(1), new(2)];
        Element[] b = [new(1), new(2)];
        Element[] c = [new(1), new(3)];

        ValueEquality.Array(a, b).Should().BeTrue();
        ValueEquality.ArrayHash(a).Should().Be(ValueEquality.ArrayHash(b));
        ValueEquality.Array(a, c).Should().BeFalse();
        ValueEquality.Array(a, [a[0]]).Should().BeFalse("a different length is a difference");
    }

    [Fact]
    public void AList_ComparesElementWise_ThroughTheElementsEquality()
    {
        ValueEquality.List(new List<Element> { new(1) }, new List<Element> { new(1) }).Should().BeTrue();
        ValueEquality.List(new List<Element> { new(1) }, new List<Element> { new(2) }).Should().BeFalse();
    }

    /// <summary>D6 / behaviour to preserve #4: comparison is by the declared shape, never the runtime type.</summary>
    [Fact]
    public void AnIReadOnlyList_EqualsAnotherBackedByADifferentRuntimeType()
    {
        IReadOnlyList<string> array = new[] { "a", "b" };
        IReadOnlyList<string> list = new List<string> { "a", "b" };
        IReadOnlyList<string> immutable = ImmutableArray.Create("a", "b");

        ValueEquality.List(array, list).Should().BeTrue();
        ValueEquality.List(list, immutable).Should().BeTrue();
        ValueEquality.ListHash(array).Should().Be(ValueEquality.ListHash(list));
        ValueEquality.ListHash(list).Should().Be(ValueEquality.ListHash(immutable));
    }

    #endregion

    #region Sequences

    [Fact]
    public void Sequence_ComparesLazyEnumerablesElementWise()
    {
        static IEnumerable<int> Yield(params int[] items) { foreach (var i in items) yield return i; }

        ValueEquality.Sequence(Yield(1, 2), Yield(1, 2)).Should().BeTrue();
        ValueEquality.Sequence(Yield(1, 2), Yield(2, 1)).Should().BeFalse();
        ValueEquality.Sequence(Yield(1, 2), Yield(1, 2, 3)).Should().BeFalse();
        ValueEquality.Sequence(Yield(1, 2, 3), Yield(1, 2)).Should().BeFalse();
        ValueEquality.SequenceHash(Yield(1, 2)).Should().Be(ValueEquality.SequenceHash(Yield(1, 2)));
    }

    [Fact]
    public void Sequence_OfDifferentRuntimeTypes_ComparesAndHashesTheSame()
    {
        IEnumerable<int> set = new HashSet<int> { 1 };
        IEnumerable<int> array = new[] { 1 };

        ValueEquality.Sequence(set, array).Should().BeTrue();
        ValueEquality.SequenceHash(set).Should().Be(ValueEquality.SequenceHash(array));
    }

    #endregion

    #region Dictionaries (from ValueComparerTests: DictionaryValueComparer_*, KeyValuePairValueComparer_ComparesBothHalves)

    /// <summary>D6: dictionaries compare order-insensitively, and so does their hash.</summary>
    [Fact]
    public void Dictionary_IgnoresInsertionOrder()
    {
        IReadOnlyDictionary<string, int> left = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        IReadOnlyDictionary<string, int> right = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };

        ValueEquality.Dictionary(left, right).Should().BeTrue();
        ValueEquality.DictionaryHash(left).Should().Be(ValueEquality.DictionaryHash(right));
        ValueEquality.DictionaryComparer<string, int>.Instance.Equals(left, right).Should().BeTrue();
    }

    [Fact]
    public void Dictionary_DetectsADifferentValue()
    {
        IDictionary<string, int> left = new Dictionary<string, int> { ["a"] = 1 };
        IDictionary<string, int> right = new Dictionary<string, int> { ["a"] = 2 };

        ValueEquality.Dictionary(left, right).Should().BeFalse();
    }

    [Fact]
    public void Dictionary_DetectsADifferentKeySet()
    {
        IReadOnlyDictionary<string, int> left = new Dictionary<string, int> { ["a"] = 1 };
        IReadOnlyDictionary<string, int> right = new Dictionary<string, int> { ["b"] = 1 };

        ValueEquality.Dictionary(left, right).Should().BeFalse();
    }

    [Fact]
    public void Dictionary_ComparesValuesThroughTheValueComparer()
    {
        IReadOnlyDictionary<string, int[]> left = new Dictionary<string, int[]> { ["a"] = [1, 2] };
        IReadOnlyDictionary<string, int[]> right = new Dictionary<string, int[]> { ["a"] = [1, 2] };

        ValueEquality.Dictionary(left, right).Should().BeFalse("arrays compare by reference without an element comparer");
        ValueEquality.Dictionary(left, right, ValueEquality.ArrayComparer<int>.Instance).Should().BeTrue();
        ValueEquality.DictionaryHash(left, ValueEquality.ArrayComparer<int>.Instance)
            .Should().Be(ValueEquality.DictionaryHash(right, ValueEquality.ArrayComparer<int>.Instance));
    }

    /// <summary>Replaces KeyValuePairValueComparer_ComparesBothHalves: a sequence of pairs compares key and value.</summary>
    [Fact]
    public void ASequenceOfKeyValuePairs_ComparesBothHalves()
    {
        ValueEquality.Array([new KeyValuePair<string, int>("a", 1)], new[] { new KeyValuePair<string, int>("a", 1) }).Should().BeTrue();
        ValueEquality.Array([new KeyValuePair<string, int>("a", 1)], new[] { new KeyValuePair<string, int>("a", 2) }).Should().BeFalse();
        ValueEquality.Array([new KeyValuePair<string, int>("a", 1)], new[] { new KeyValuePair<string, int>("b", 1) }).Should().BeFalse();
    }

    #endregion

    #region Element comparers and nesting (from ValueComparerTests: DefaultValueComparer_*, ReferenceEqualityComparer_*; ComparerRegistryResolutionTests: NestedIsEquals_UsesTheAttributeComparer)

    /// <summary>Replaces DefaultValueComparer_DelegatesToTheDefaultComparer: no element comparer means the default one.</summary>
    [Fact]
    public void WithoutAnElementComparer_ElementsCompareThroughEqualityComparerDefault()
    {
        ValueEquality.Array([5], new[] { 5 }).Should().BeTrue();
        ValueEquality.Array([5], new[] { 6 }).Should().BeFalse();
        ValueEquality.Array([new Element(5)], new[] { new Element(5) }).Should().BeTrue();
    }

    /// <summary>
    /// Replaces ReferenceEqualityComparer_ComparesByIdentity and For_ForAnUnregisteredType_FallsBackToDefault: an
    /// element type without value equality compares by reference. This is the case MINT005 warns about.
    /// </summary>
    [Fact]
    public void AnElementWithoutValueEquality_ComparesByReference()
    {
        var shared = new ReferenceOnly(1);

        ValueEquality.Array([shared], new[] { shared }).Should().BeTrue();
        ValueEquality.Array([new ReferenceOnly(1)], new[] { new ReferenceOnly(1) }).Should().BeFalse();
    }

    /// <summary>Behaviour to preserve #7: nested collections compose through the *Comparer&lt;T&gt; instances.</summary>
    [Fact]
    public void NestedCollections_ComposeThroughElementComparers()
    {
        var inner = ValueEquality.ImmutableArrayComparer<Element>.Instance;
        var a = ImmutableArray.Create(ImmutableArray.Create(new Element(1)), ImmutableArray.Create(new Element(2)));
        var b = ImmutableArray.Create(ImmutableArray.Create(new Element(1)), ImmutableArray.Create(new Element(2)));
        var c = ImmutableArray.Create(ImmutableArray.Create(new Element(1)), ImmutableArray.Create(new Element(3)));

        ValueEquality.ImmutableArray(a, b, inner).Should().BeTrue();
        ValueEquality.ImmutableArrayHash(a, inner).Should().Be(ValueEquality.ImmutableArrayHash(b, inner));
        ValueEquality.ImmutableArray(a, c, inner).Should().BeFalse();

        var lists = new ValueEquality.ListComparer<string[]>(ValueEquality.ArrayComparer<string>.Instance);
        lists.Equals(new List<string[]> { new[] { "x" } }, new List<string[]> { new[] { "x" } }).Should().BeTrue();
        lists.Equals(new List<string[]> { new[] { "x" } }, new List<string[]> { new[] { "y" } }).Should().BeFalse();
        lists.GetHashCode(new List<string[]> { new[] { "x" } }).Should().Be(lists.GetHashCode(new List<string[]> { new[] { "x" } }));
    }

    [Fact]
    public void ASequenceComparer_WithAnElementComparer_ComparesNestedSequences()
    {
        var comparer = new ValueEquality.SequenceComparer<int[]>(ValueEquality.ArrayComparer<int>.Instance);

        comparer.Equals(new HashSet<int[]> { new[] { 1 } }, new List<int[]> { new[] { 1 } }).Should().BeTrue();
        comparer.Equals(new List<int[]> { new[] { 1 } }, new List<int[]> { new[] { 2 } }).Should().BeFalse();
    }

    #endregion

    #region Hash folding (from HashCodeCompatTests, whose type is deleted: hashing is an inline h * 31 + x fold)

    [Fact]
    public void Combine_IsDeterministic()
        => ValueEquality.Combine(1, 2).Should().Be(ValueEquality.Combine(1, 2));

    [Fact]
    public void Combine_IsOrderSensitive()
        => ValueEquality.Combine(ValueEquality.Combine(17, 1), 2).Should().NotBe(ValueEquality.Combine(ValueEquality.Combine(17, 2), 1));

    [Fact]
    public void SequenceHash_IsDeterministic()
        => ValueEquality.SequenceHash(new object[] { "a", 42 }).Should().Be(ValueEquality.SequenceHash(new object[] { "a", 42 }));

    [Fact]
    public void ArrayHash_DistinguishesDifferentOrders()
        => ValueEquality.ArrayHash(["a", "b"]).Should().NotBe(ValueEquality.ArrayHash(["b", "a"]));

    [Fact]
    public void ANullElement_HashesAsZero()
        => ValueEquality.ArrayHash(new string?[] { null }).Should().Be(ValueEquality.ArrayHash(new[] { 0 }, new ZeroHash()));

    [Fact]
    public void AnExplicitElementComparer_IsUsedForTheHash()
        => ValueEquality.ArrayHash(["abc"], StringComparer.OrdinalIgnoreCase)
            .Should().Be(ValueEquality.ArrayHash(["ABC"], StringComparer.OrdinalIgnoreCase));

    /// <summary>A null collection hashes to 0; an empty one does not, so null and empty stay distinguishable.</summary>
    [Fact]
    public void ANullCollection_HashesToZero()
    {
        ValueEquality.ArrayHash<int>(null).Should().Be(0);
        ValueEquality.ListHash<int>(null).Should().Be(0);
        ValueEquality.SequenceHash<int>(null).Should().Be(0);
        ValueEquality.ArrayHash(System.Array.Empty<int>()).Should().NotBe(0);
    }

    private sealed class ZeroHash : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => x == y;
        public int GetHashCode(int obj) => 0;
    }

    #endregion
}
