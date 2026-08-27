using System.Collections.Immutable;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// ComparerRegistry is a process-wide static, seeded by a [ModuleInitializer]. Every test
/// that registers into it lives in this one collection so xUnit runs them serially and they
/// cannot interfere with each other.
/// </summary>
[CollectionDefinition(nameof(ComparerRegistryCollection), DisableParallelization = true)]
public class ComparerRegistryCollection;

[Collection(nameof(ComparerRegistryCollection))]
public class ComparerRegistryTests
{
    private sealed class Marker
    {
        public int Value { get; set; }
    }

    private sealed class MarkerComparer : IEqualityComparer<Marker>
    {
        public bool Equals(Marker? x, Marker? y) => x?.Value == y?.Value;
        public int GetHashCode(Marker obj) => obj.Value;
    }

    [Fact]
    public void TryGet_ForAnUnregisteredType_IsFalse()
        => ComparerRegistry.TryGet<Uri>(out _).Should().BeFalse();

    [Fact]
    public void For_ForAnUnregisteredType_FallsBackToDefault()
        => ComparerRegistry.For<int>().Should().BeSameAs(EqualityComparer<int>.Default);

    [Fact]
    public void Register_ThenTryGet_ReturnsTheComparer()
    {
        var comparer = new MarkerComparer();
        ComparerRegistry.Register(typeof(Marker), comparer);

        ComparerRegistry.TryGet<Marker>(out var found).Should().BeTrue();
        found.Should().BeSameAs(comparer);
    }

    [Fact]
    public void Register_Overwrites()
    {
        ComparerRegistry.Register(typeof(Marker), new MarkerComparer());
        var second = new MarkerComparer();
        ComparerRegistry.Register(typeof(Marker), second);

        ComparerRegistry.TryGet<Marker>(out var found).Should().BeTrue();
        found.Should().BeSameAs(second);
    }

    [Fact]
    public void TryRegister_ReportsFalseForASecondRegistration()
    {
        ComparerRegistry.Register(typeof(Marker), new MarkerComparer());

        ComparerRegistry.TryRegister(typeof(Marker), new MarkerComparer()).Should().BeFalse();
    }

    [Fact]
    public void Register_WithNullType_Throws()
    {
        var act = () => ComparerRegistry.Register(null!, new MarkerComparer());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Register_WithNullComparer_Throws()
    {
        var act = () => ComparerRegistry.Register(typeof(Marker), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Register_WithAComparerForTheWrongType_Throws()
    {
        var act = () => ComparerRegistry.Register(typeof(Marker), new MarkerComparerForSomethingElse());

        act.Should().Throw<ArgumentException>().WithMessage("*must implement*");
    }

    [Fact]
    public void TryRegister_WithNulls_ReportsFalseRatherThanThrowing()
    {
        ComparerRegistry.TryRegister(null!, new MarkerComparer()).Should().BeFalse();
        ComparerRegistry.TryRegister(typeof(Marker), null!).Should().BeFalse();
    }

    [Fact]
    public void TryRegister_WithAComparerForTheWrongType_Throws()
    {
        var act = () => ComparerRegistry.TryRegister(typeof(Uri), new MarkerComparerForSomethingElse());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheModuleInitializerRegistersTheBuiltInComparers()
    {
        ComparerRegistry.TryGet<Models.AnalyzerInfo>(out _).Should().BeTrue();
        ComparerRegistry.TryGet<Models.LangVersion>(out _).Should().BeTrue();
        ComparerRegistry.TryGet<Settings>(out _).Should().BeTrue();
    }

    private sealed class MarkerComparerForSomethingElse : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => x == y;
        public int GetHashCode(string obj) => obj.GetHashCode();
    }
}

public class BuiltInValueComparerTests
{
    private sealed class ProbeComparer<T> : ValueComparer<T>
    {
        public bool CallIsEquals(T x, T y) => IsEquals(x, y);
    }

    /// <summary>
    /// Routes the instance hash through the protected STATIC AddHash&lt;TProp&gt; helper -- the
    /// one a generated comparer calls per property, and the one that carries the structural
    /// ImmutableArray/list handling. The base virtual AddHash deliberately does not: it falls
    /// back to obj.GetHashCode().
    /// </summary>
    private sealed class StructuralHashProbe<T> : ValueComparer<T>
    {
        protected override void AddHash(ref Polyfills.HashCodeCompat h, T? obj) => AddHash<T?>(ref h, obj);
    }

    #region ValueComparer base behaviour

    [Fact]
    public void Equals_ForBothNull_IsTrue()
        => new ProbeComparer<string>().Equals(null, null).Should().BeTrue();

    [Fact]
    public void Equals_ForExactlyOneNull_IsFalse()
    {
        var comparer = new ProbeComparer<string>();

        comparer.Equals("a", null).Should().BeFalse();
        comparer.Equals(null, "a").Should().BeFalse();
    }

    [Fact]
    public void Equals_ForTheSameReference_IsTrue()
    {
        var instance = new Uri("https://example.test");

        new ProbeComparer<Uri>().Equals(instance, instance).Should().BeTrue();
    }

    [Fact]
    public void AreEqual_DefaultsToTrue_WhenNotOverridden()
    {
        // The base AreEqual returns true, so a comparer that forgets to override it treats
        // every non-null pair as equal. Pinned because it is a sharp edge for anyone
        // writing a new comparer.
        new ProbeComparer<Uri>()
            .Equals(new Uri("https://a.test"), new Uri("https://b.test"))
            .Should().BeTrue();
    }

    #endregion

    #region IsEquals dispatch

    [Fact]
    public void IsEquals_UsesTheDefaultComparerForPrimitives()
    {
        var probe = new ProbeComparer<int>();

        probe.CallIsEquals(1, 1).Should().BeTrue();
        probe.CallIsEquals(1, 2).Should().BeFalse();
    }

    [Fact]
    public void IsEquals_ComparesStringsByValue()
    {
        var probe = new ProbeComparer<string>();

        probe.CallIsEquals("abc", string.Concat("ab", "c")).Should().BeTrue();
        probe.CallIsEquals("abc", "abd").Should().BeFalse();
    }

    /// <summary>
    /// Regression for D15 in docs/PRD-TestCoverage.md. The ImmutableArray branch of IsEquals
    /// passed TProp -- the ImmutableArray type itself -- where the ELEMENT type was expected,
    /// so ImmutableArrayEquals cast ImmutableArray&lt;int&gt; to
    /// ImmutableArray&lt;ImmutableArray&lt;int&gt;&gt; and threw InvalidCastException. Every
    /// ImmutableArray-valued property comparison failed at runtime.
    /// </summary>
    [Fact]
    public void IsEquals_ComparesImmutableArraysStructurally()
    {
        var probe = new ProbeComparer<ImmutableArray<int>>();

        probe.CallIsEquals([1, 2, 3], [1, 2, 3]).Should().BeTrue();
        probe.CallIsEquals([1, 2, 3], [1, 2, 4]).Should().BeFalse();
        probe.CallIsEquals([1, 2], [1, 2, 3]).Should().BeFalse();
        probe.CallIsEquals([], []).Should().BeTrue();
    }

    [Fact]
    public void IsEquals_ComparesImmutableArraysOfStringsStructurally()
    {
        var probe = new ProbeComparer<ImmutableArray<string>>();

        probe.CallIsEquals(["a", "b"], ["a", "b"]).Should().BeTrue();
        probe.CallIsEquals(["a", "b"], ["b", "a"]).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_ForAnImmutableArray_IsStructuralAndDoesNotThrow()
    {
        // The AddHash side had the identical element-type bug, reached through the static
        // AddHash<TProp> helper that generated comparers call per property.
        var comparer = new StructuralHashProbe<ImmutableArray<int>>();

        comparer.GetHashCode([1, 2, 3]).Should().Be(comparer.GetHashCode([1, 2, 3]));
        comparer.GetHashCode([1, 2, 3]).Should().NotBe(comparer.GetHashCode([3, 2, 1]));
    }

    [Fact]
    public void GetHashCode_ForAList_IsStructural()
    {
        var comparer = new StructuralHashProbe<List<string>>();

        comparer.GetHashCode(["a", "b"]).Should().Be(comparer.GetHashCode(["a", "b"]));
        comparer.GetHashCode(["a", "b"]).Should().NotBe(comparer.GetHashCode(["b", "a"]));
    }

    /// <summary>
    /// The base virtual AddHash is a documented best-effort fallback: it calls
    /// obj.GetHashCode(), and ImmutableArray hashes by its underlying array reference. So a
    /// comparer that does NOT route through the static helper has a structural Equals and a
    /// non-structural GetHashCode. Pinned so the asymmetry is visible rather than surprising.
    /// </summary>
    [Fact]
    public void GetHashCode_WithoutTheStaticHelper_IsNotStructural()
    {
        var comparer = new ProbeComparer<ImmutableArray<int>>();

        comparer.GetHashCode([1, 2, 3]).Should().NotBe(comparer.GetHashCode([1, 2, 3]));
    }

    [Fact]
    public void IsEquals_ComparesListsStructurally()
    {
        var probe = new ProbeComparer<List<string>>();

        probe.CallIsEquals(["a", "b"], ["a", "b"]).Should().BeTrue();
        probe.CallIsEquals(["a", "b"], ["b", "a"]).Should().BeFalse();
        probe.CallIsEquals(["a"], ["a", "b"]).Should().BeFalse();
    }

    [Fact]
    public void IsEquals_ComparesArraysStructurally()
    {
        var probe = new ProbeComparer<int[]>();

        probe.CallIsEquals([1, 2], [1, 2]).Should().BeTrue();
        probe.CallIsEquals([1, 2], [2, 1]).Should().BeFalse();
    }

    [Fact]
    public void IsEquals_ForTwoNulls_IsTrue()
        => new ProbeComparer<string>().CallIsEquals(null!, null!).Should().BeTrue();

    [Fact]
    public void IsEquals_ForOneNull_IsFalse()
        => new ProbeComparer<string>().CallIsEquals("a", null!).Should().BeFalse();

    #endregion

    #region GetHashCode

    [Fact]
    public void GetHashCode_ForNull_IsStable()
    {
        var comparer = new ProbeComparer<string>();

        comparer.GetHashCode(null).Should().Be(comparer.GetHashCode(null));
    }

    [Fact]
    public void GetHashCode_IsConsistentForEqualValues()
    {
        var comparer = new ProbeComparer<string>();

        comparer.GetHashCode("abc").Should().Be(comparer.GetHashCode(string.Concat("ab", "c")));
    }

    #endregion

    #region ArrayValueComparer / ListValueComparer / DictionaryValueComparer

    [Fact]
    public void ArrayValueComparer_ComparesElementwise()
    {
        var comparer = new ArrayValueComparer<string>();

        comparer.Equals(["a", "b"], ["a", "b"]).Should().BeTrue();
        comparer.Equals(["a", "b"], ["a", "c"]).Should().BeFalse();
        comparer.Equals(["a"], ["a", "b"]).Should().BeFalse();
        comparer.Equals([], []).Should().BeTrue();
    }

    [Fact]
    public void ListValueComparer_ComparesElementwise()
    {
        var comparer = new ListValueComparer<int>();

        comparer.Equals([1, 2], [1, 2]).Should().BeTrue();
        comparer.Equals([1, 2], [1, 3]).Should().BeFalse();
        comparer.Equals([], []).Should().BeTrue();
    }

    [Fact]
    public void DictionaryValueComparer_IgnoresInsertionOrder()
    {
        var comparer = new DictionaryValueComparer<string, int>();

        var left = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var right = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };

        comparer.Equals(left, right).Should().BeTrue();
    }

    [Fact]
    public void DictionaryValueComparer_DetectsADifferentValue()
    {
        var comparer = new DictionaryValueComparer<string, int>();

        comparer.Equals(
            new Dictionary<string, int> { ["a"] = 1 },
            new Dictionary<string, int> { ["a"] = 2 }).Should().BeFalse();
    }

    [Fact]
    public void DictionaryValueComparer_DetectsADifferentKeySet()
    {
        var comparer = new DictionaryValueComparer<string, int>();

        comparer.Equals(
            new Dictionary<string, int> { ["a"] = 1 },
            new Dictionary<string, int> { ["b"] = 1 }).Should().BeFalse();
    }

    [Fact]
    public void KeyValuePairValueComparer_ComparesBothHalves()
    {
        var comparer = new KeyValuePairValueComparer<string, int>();

        comparer.Equals(new("a", 1), new("a", 1)).Should().BeTrue();
        comparer.Equals(new("a", 1), new("a", 2)).Should().BeFalse();
        comparer.Equals(new("a", 1), new("b", 1)).Should().BeFalse();
    }

    [Fact]
    public void DefaultValueComparer_DelegatesToTheDefaultComparer()
    {
        var comparer = new DefaultValueComparer<int>();

        comparer.Equals(5, 5).Should().BeTrue();
        comparer.Equals(5, 6).Should().BeFalse();
    }

    [Fact]
    public void ReferenceEqualityComparer_ComparesByIdentity()
    {
        var comparer = ReferenceEqualityComparer<string>.Instance;
        var first = new string(['a']);
        var second = new string(['a']);

        comparer.Equals(first, first).Should().BeTrue();
        comparer.Equals(first, second).Should().BeFalse();
    }

    #endregion
}

public class HashCodeCompatTests
{
    [Fact]
    public void Combine_IsDeterministic()
        => Polyfills.HashCodeCompat.Combine(1, 2).Should().Be(Polyfills.HashCodeCompat.Combine(1, 2));

    [Fact]
    public void Combine_IsOrderSensitive()
        => Polyfills.HashCodeCompat.Combine(1, 2).Should().NotBe(Polyfills.HashCodeCompat.Combine(2, 1));

    [Fact]
    public void Add_ThenToHashCode_IsDeterministic()
    {
        int Hash()
        {
            var h = new Polyfills.HashCodeCompat();
            h.Add("a");
            h.Add(42);
            return h.ToHashCode();
        }

        Hash().Should().Be(Hash());
    }

    [Fact]
    public void Add_DistinguishesDifferentSequences()
    {
        var first = new Polyfills.HashCodeCompat();
        first.Add("a");
        first.Add("b");

        var second = new Polyfills.HashCodeCompat();
        second.Add("b");
        second.Add("a");

        first.ToHashCode().Should().NotBe(second.ToHashCode());
    }

    [Fact]
    public void Add_TreatsNullAsZero()
    {
        var withNull = new Polyfills.HashCodeCompat();
        withNull.Add<string?>(null);

        var withDefault = new Polyfills.HashCodeCompat();
        withDefault.Add(0);

        withNull.ToHashCode().Should().Be(withDefault.ToHashCode());
    }

    [Fact]
    public void Add_WithAnExplicitComparer_UsesIt()
    {
        var comparer = StringComparer.OrdinalIgnoreCase;

        var lower = new Polyfills.HashCodeCompat();
        lower.Add("abc", comparer);

        var upper = new Polyfills.HashCodeCompat();
        upper.Add("ABC", comparer);

        lower.ToHashCode().Should().Be(upper.ToHashCode());
    }

    [Fact]
    public void ToHashCode_OnAFreshInstance_IsZero()
        => new Polyfills.HashCodeCompat().ToHashCode().Should().Be(0);
}
