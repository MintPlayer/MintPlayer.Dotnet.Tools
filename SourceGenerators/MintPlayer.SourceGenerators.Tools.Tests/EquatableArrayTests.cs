using System.Collections.Immutable;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// <see cref="EquatableArray{T}"/>: what a pipeline step returns when it builds a new collection, so the step
/// compares by value instead of by array reference.
/// </summary>
public class EquatableArrayTests
{
    [Fact]
    public void TwoArraysWithEqualElements_AreEqual_AndHashEqually()
    {
        EquatableArray<string> a = new[] { "a", "b" };
        EquatableArray<string> b = new[] { "a", "b" };

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
        a.GetHashCode().Should().Be(b.GetHashCode());
        EqualityComparer<EquatableArray<string>>.Default.Equals(a, b).Should().BeTrue();
    }

    [Fact]
    public void ADifferentElementOrLength_IsUnequal()
    {
        EquatableArray<string> a = new[] { "a", "b" };

        a.Equals(new EquatableArray<string>(new[] { "b", "a" })).Should().BeFalse();
        a.Equals(new EquatableArray<string>(new[] { "a" })).Should().BeFalse();
        (a != new EquatableArray<string>(new[] { "a" })).Should().BeTrue();
    }

    [Fact]
    public void ADefaultArray_BehavesAsEmpty_AndEqualsEmpty()
    {
        var defaultArray = default(EquatableArray<int>);

        defaultArray.Count.Should().Be(0);
        defaultArray.IsEmpty.Should().BeTrue();
        defaultArray.Equals(EquatableArray<int>.Empty).Should().BeTrue();
        defaultArray.GetHashCode().Should().Be(EquatableArray<int>.Empty.GetHashCode());
        defaultArray.AsImmutableArray().IsDefault.Should().BeFalse();
        defaultArray.AsImmutableArray().IsEmpty.Should().BeTrue();
        defaultArray.ToList().Count.Should().Be(0);
    }

    [Fact]
    public void ItConvertsFromArraysImmutableArraysAndSequences()
    {
        var expected = new EquatableArray<int>(new[] { 1, 2 });

        ((EquatableArray<int>)ImmutableArray.Create(1, 2)).Equals(expected).Should().BeTrue();
        ((EquatableArray<int>)default(ImmutableArray<int>)).Equals(EquatableArray<int>.Empty).Should().BeTrue();
        new[] { 1, 2 }.Where(_ => true).ToEquatableArray().Equals(expected).Should().BeTrue();
        ImmutableArray.Create(1, 2).ToEquatableArray().Equals(expected).Should().BeTrue();
        new EquatableArray<int>((IEnumerable<int>)new List<int> { 1, 2 }).Equals(expected).Should().BeTrue();
    }

    [Fact]
    public void ItExposesItsElements()
    {
        EquatableArray<string> array = new[] { "a", "b" };

        array.Count.Should().Be(2);
        array[1].Should().Be("b");
        string.Join(",", array).Should().Be("a,b");
        array.AsImmutableArray().Should().Equal("a", "b");
    }

    [Fact]
    public void Equals_Object_OnlyMatchesAnEquatableArray()
    {
        EquatableArray<int> array = new[] { 1 };

        array.Equals((object)new EquatableArray<int>(new[] { 1 })).Should().BeTrue();
        array.Equals((object)new[] { 1 }).Should().BeFalse();
        array.Equals((object?)null).Should().BeFalse();
    }

    [Fact]
    public void ElementsCompareByTheirOwnEquality()
    {
        EquatableArray<LocationKey> a = new[] { new LocationKey("/a.cs", 1, 2, 3, 4) };
        EquatableArray<LocationKey> b = new[] { new LocationKey("/a.cs", 1, 2, 3, 4) };

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
