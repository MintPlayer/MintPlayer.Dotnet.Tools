using System.Collections.Immutable;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// How <see cref="ComparerRegistry"/> finds a comparer nobody registered: from a type's
/// <see cref="ValueComparerAttribute"/>, or structurally for arrays, lists, immutable arrays and
/// value tuples.
/// </summary>
/// <remarks>
/// <para>
/// This resolution is what makes every generator pipeline in the repo cache at all. The generated
/// <c>WithComparer()</c>, <see cref="ComparerRegistry.For{T}"/> and the nested
/// <c>ValueComparer&lt;T&gt;.IsEquals</c> all go through it; before it existed they all fell back to
/// <see cref="EqualityComparer{T}.Default"/>, i.e. reference equality for every model class.
/// </para>
/// <para>
/// The registry is process-global and caches both hits and misses, so every test uses marker types
/// of its own: a type another test already resolved would make the test order-dependent. The
/// collection keeps these serial with the other registry tests.
/// </para>
/// </remarks>
[Collection(nameof(ComparerRegistryCollection))]
public class ComparerRegistryResolutionTests
{
    #region Marker types

    [ValueComparer(typeof(WithInstanceComparer))]
    private sealed class WithInstance { public int Value { get; set; } }

    private sealed class WithInstanceComparer : ValueComparer<WithInstance>
    {
        public static readonly WithInstanceComparer Instance = new();
        private WithInstanceComparer() { }
        protected override bool AreEqual(WithInstance x, WithInstance y) => x.Value == y.Value;
    }

    [ValueComparer(typeof(PrivateCtorComparer))]
    private sealed class WithPrivateCtor { public int Value { get; set; } }

    private sealed class PrivateCtorComparer : ValueComparer<WithPrivateCtor>
    {
        private PrivateCtorComparer() { }
        protected override bool AreEqual(WithPrivateCtor x, WithPrivateCtor y) => x.Value == y.Value;
    }

    [ValueComparer(typeof(RegisteredFirstAttributeComparer))]
    private sealed class RegisteredFirst { public int Value { get; set; } }

    private sealed class RegisteredFirstAttributeComparer : ValueComparer<RegisteredFirst>
    {
        protected override bool AreEqual(RegisteredFirst x, RegisteredFirst y) => x.Value == y.Value;
    }

    [ValueComparer(typeof(ResolvedFirstAttributeComparer))]
    private sealed class ResolvedFirst { public int Value { get; set; } }

    private sealed class ResolvedFirstAttributeComparer : ValueComparer<ResolvedFirst>
    {
        protected override bool AreEqual(ResolvedFirst x, ResolvedFirst y) => x.Value == y.Value;
    }

    [ValueComparer(typeof(ResolvedFirstForRegisterAttributeComparer))]
    private sealed class ResolvedFirstForRegister { public int Value { get; set; } }

    private sealed class ResolvedFirstForRegisterAttributeComparer : ValueComparer<ResolvedFirstForRegister>
    {
        protected override bool AreEqual(ResolvedFirstForRegister x, ResolvedFirstForRegister y) => x.Value == y.Value;
    }

    private sealed class ExplicitComparer<T> : IEqualityComparer<T>
    {
        public bool Equals(T? x, T? y) => true;
        public int GetHashCode(T obj) => 0;
    }

    private sealed class NoAttribute { public int Value { get; set; } }

    private sealed class NoAttributeThenRegistered { public int Value { get; set; } }

    [ValueComparer(typeof(ElementComparer))]
    private sealed class Element { public int Value { get; set; } }

    private sealed class ElementComparer : ValueComparer<Element>
    {
        protected override bool AreEqual(Element x, Element y) => x.Value == y.Value;
        protected override void AddHash(ref Polyfills.HashCodeCompat h, Element? obj) => h.Add(obj?.Value ?? 0);
    }

    [ValueComparer(typeof(NestedComparer))]
    private sealed class Nested { public int Value { get; set; } }

    private sealed class NestedComparer : ValueComparer<Nested>
    {
        protected override bool AreEqual(Nested x, Nested y) => x.Value == y.Value;
    }

    private sealed class Holder { public Nested Inner { get; set; } = new(); }

    /// <summary>Compares a <see cref="Holder"/> the way a generated comparer does: property by property through IsEquals.</summary>
    private sealed class HolderComparer : ValueComparer<Holder>
    {
        protected override bool AreEqual(Holder x, Holder y) => IsEquals(x.Inner, y.Inner);
    }

    #endregion

    #region [ValueComparer] attribute

    [Fact]
    public void AnAttributeComparerWithAStaticInstance_ResolvesToThatInstance()
    {
        ComparerRegistry.TryGet<WithInstance>(out var comparer).Should().BeTrue();
        comparer.Should().BeSameAs(WithInstanceComparer.Instance);
        comparer.Equals(new WithInstance { Value = 1 }, new WithInstance { Value = 1 }).Should().BeTrue();
    }

    [Fact]
    public void AnAttributeComparerWithOnlyANonPublicConstructor_IsInstantiated()
    {
        var comparer = ComparerRegistry.For<WithPrivateCtor>();

        comparer.Should().BeOfType<PrivateCtorComparer>();
        comparer.Equals(new WithPrivateCtor { Value = 1 }, new WithPrivateCtor { Value = 1 }).Should().BeTrue();
        comparer.Equals(new WithPrivateCtor { Value = 1 }, new WithPrivateCtor { Value = 2 }).Should().BeFalse();
    }

    [Fact]
    public void AnAttributeComparer_IsResolvedOnce()
        => ComparerRegistry.For<WithPrivateCtor>().Should().BeSameAs(ComparerRegistry.For<WithPrivateCtor>());

    #endregion

    #region Explicit registration wins, in either order

    [Fact]
    public void AnExplicitRegistration_BeforeFirstUse_BeatsTheAttribute()
    {
        var explicitComparer = new ExplicitComparer<RegisteredFirst>();
        ComparerRegistry.Register(typeof(RegisteredFirst), explicitComparer);

        ComparerRegistry.For<RegisteredFirst>().Should().BeSameAs(explicitComparer);
    }

    [Fact]
    public void TryRegister_AfterTheAttributeWasResolved_ReplacesIt()
    {
        ComparerRegistry.For<ResolvedFirst>().Should().BeOfType<ResolvedFirstAttributeComparer>(
            "the first lookup resolves the attribute");

        var explicitComparer = new ExplicitComparer<ResolvedFirst>();
        ComparerRegistry.TryRegister(typeof(ResolvedFirst), explicitComparer).Should().BeTrue(
            "an implicit resolution must not count as a registration");

        ComparerRegistry.For<ResolvedFirst>().Should().BeSameAs(explicitComparer);

        // ...and once it is explicit, a second TryRegister does not replace it.
        ComparerRegistry.TryRegister(typeof(ResolvedFirst), new ExplicitComparer<ResolvedFirst>()).Should().BeFalse();
        ComparerRegistry.For<ResolvedFirst>().Should().BeSameAs(explicitComparer);
    }

    [Fact]
    public void Register_AfterTheAttributeWasResolved_ReplacesIt()
    {
        ComparerRegistry.For<ResolvedFirstForRegister>().Should().BeOfType<ResolvedFirstForRegisterAttributeComparer>();

        var explicitComparer = new ExplicitComparer<ResolvedFirstForRegister>();
        ComparerRegistry.Register(typeof(ResolvedFirstForRegister), explicitComparer);

        ComparerRegistry.For<ResolvedFirstForRegister>().Should().BeSameAs(explicitComparer);
    }

    #endregion

    #region Negative cache

    [Fact]
    public void ATypeWithoutTheAttribute_FallsBackToDefault_AndStaysThere()
    {
        ComparerRegistry.TryGet<NoAttribute>(out _).Should().BeFalse();
        ComparerRegistry.For<NoAttribute>().Should().BeSameAs(EqualityComparer<NoAttribute>.Default);

        // The miss is cached; asking again must give the same answer, not a late resolution.
        ComparerRegistry.TryGet<NoAttribute>(out _).Should().BeFalse();
        ComparerRegistry.For<NoAttribute>().Should().BeSameAs(EqualityComparer<NoAttribute>.Default);
    }

    [Fact]
    public void ACachedMiss_DoesNotBlockALaterRegistration()
    {
        ComparerRegistry.TryGet<NoAttributeThenRegistered>(out _).Should().BeFalse();

        var explicitComparer = new ExplicitComparer<NoAttributeThenRegistered>();
        ComparerRegistry.Register(typeof(NoAttributeThenRegistered), explicitComparer);

        ComparerRegistry.For<NoAttributeThenRegistered>().Should().BeSameAs(explicitComparer);
    }

    #endregion

    #region Structural collections

    [Fact]
    public void AnArray_ComparesElementWise_ThroughTheElementsComparer()
    {
        var comparer = ComparerRegistry.For<Element[]>();

        Element[] a = [new() { Value = 1 }, new() { Value = 2 }];
        Element[] b = [new() { Value = 1 }, new() { Value = 2 }];
        Element[] c = [new() { Value = 1 }, new() { Value = 3 }];

        comparer.Equals(a, b).Should().BeTrue();
        comparer.GetHashCode(a).Should().Be(comparer.GetHashCode(b));
        comparer.Equals(a, c).Should().BeFalse();
        comparer.Equals(a, [a[0]]).Should().BeFalse("a different length is a difference");
    }

    [Fact]
    public void AList_ComparesElementWise_ThroughTheElementsComparer()
    {
        var comparer = ComparerRegistry.For<List<Element>>();

        comparer.Equals([new() { Value = 1 }], [new() { Value = 1 }]).Should().BeTrue();
        comparer.Equals([new() { Value = 1 }], [new() { Value = 2 }]).Should().BeFalse();
    }

    [Fact]
    public void AnImmutableArray_ComparesElementWise_ThroughTheElementsComparer()
    {
        var comparer = ComparerRegistry.For<ImmutableArray<Element>>();

        var a = ImmutableArray.Create(new Element { Value = 1 });
        var b = ImmutableArray.Create(new Element { Value = 1 });

        comparer.Equals(a, b).Should().BeTrue();
        comparer.GetHashCode(a).Should().Be(comparer.GetHashCode(b));
        comparer.Equals(a, ImmutableArray.Create(new Element { Value = 2 })).Should().BeFalse();
    }

    [Fact]
    public void ADefaultImmutableArray_EqualsOnlyAnotherDefault_AndHashesWithoutThrowing()
    {
        var comparer = ComparerRegistry.For<ImmutableArray<Element>>();

        comparer.Equals(default, default).Should().BeTrue();
        comparer.Equals(default, ImmutableArray<Element>.Empty).Should().BeFalse();
        comparer.Equals(ImmutableArray<Element>.Empty, default).Should().BeFalse();
        comparer.GetHashCode(default).Should().Be(comparer.GetHashCode(default));
    }

    [Fact]
    public void AValueTuple_ComparesItemByItem_ThroughTheItemsComparers()
    {
        var comparer = ComparerRegistry.For<(Element, Element)>();

        var a = (new Element { Value = 1 }, new Element { Value = 2 });
        var b = (new Element { Value = 1 }, new Element { Value = 2 });

        // The tuple's own Equals would compare the two Elements by reference.
        a.Equals(b).Should().BeFalse();

        comparer.Equals(a, b).Should().BeTrue();
        comparer.GetHashCode(a).Should().Be(comparer.GetHashCode(b));
        comparer.Equals(a, (a.Item1, new Element { Value = 3 })).Should().BeFalse();
    }

    #endregion

    #region Nested IsEquals, LocationKey

    [Fact]
    public void NestedIsEquals_UsesTheAttributeComparer()
    {
        var comparer = new HolderComparer();

        comparer.Equals(new Holder { Inner = new() { Value = 1 } }, new Holder { Inner = new() { Value = 1 } })
            .Should().BeTrue("two distinct but equal Nested values must compare equal through their [ValueComparer]");
        comparer.Equals(new Holder { Inner = new() { Value = 1 } }, new Holder { Inner = new() { Value = 2 } })
            .Should().BeFalse();
    }

    [Fact]
    public void LocationKey_ComparesByValue()
    {
        var comparer = ComparerRegistry.For<LocationKey>();

        var a = new LocationKey("/src/A.cs", 1, 2, 3, 4);
        var b = new LocationKey("/src/A.cs", 1, 2, 3, 4);

        comparer.Equals(a, b).Should().BeTrue();
        comparer.GetHashCode(a).Should().Be(comparer.GetHashCode(b));

        comparer.Equals(a, new LocationKey("/src/B.cs", 1, 2, 3, 4)).Should().BeFalse();
        comparer.Equals(a, new LocationKey("/src/A.cs", 2, 2, 3, 4)).Should().BeFalse("a moved location is a different location");
        comparer.Equals(a, new LocationKey("/src/A.cs", 1, 2, 3, 5)).Should().BeFalse();
    }

    #endregion
}
