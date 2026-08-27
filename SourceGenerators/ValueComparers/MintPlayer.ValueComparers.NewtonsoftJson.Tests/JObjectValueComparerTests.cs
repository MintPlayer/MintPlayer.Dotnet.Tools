using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.ValueComparers.NewtonsoftJson;
using Newtonsoft.Json.Linq;

namespace MintPlayer.ValueComparers.NewtonsoftJson.Tests;

public class JObjectValueComparerTests
{
    private static readonly JObjectValueComparer Comparer = new();

    [Fact]
    public void Equals_ForIdenticalObjects_IsTrue()
        => Comparer.Equals(JObject.Parse("{ 'a': 1 }"), JObject.Parse("{ 'a': 1 }")).Should().BeTrue();

    [Fact]
    public void Equals_IgnoresInsignificantWhitespaceAndIndentation()
        => Comparer.Equals(
            JObject.Parse("{ 'a': 1, 'b': 2 }"),
            JObject.Parse("{\n  'a': 1,\n  'b': 2\n}")).Should().BeTrue();

    [Fact]
    public void Equals_ForDifferentValues_IsFalse()
        => Comparer.Equals(JObject.Parse("{ 'a': 1 }"), JObject.Parse("{ 'a': 2 }")).Should().BeFalse();

    [Fact]
    public void Equals_ForDifferentPropertyNames_IsFalse()
        => Comparer.Equals(JObject.Parse("{ 'a': 1 }"), JObject.Parse("{ 'b': 1 }")).Should().BeFalse();

    [Fact]
    public void Equals_IsSensitiveToPropertyOrder()
    {
        // JObject.ToString preserves insertion order, so this comparer treats a reordered
        // object as different. Pinned deliberately: it is the documented behaviour of a
        // string-based comparison, and a caller relying on order-insensitivity would be wrong.
        Comparer.Equals(
            JObject.Parse("{ 'a': 1, 'b': 2 }"),
            JObject.Parse("{ 'b': 2, 'a': 1 }")).Should().BeFalse();
    }

    [Fact]
    public void Equals_ForNestedObjects_ComparesDeeply()
    {
        Comparer.Equals(
            JObject.Parse("{ 'a': { 'b': { 'c': 1 } } }"),
            JObject.Parse("{ 'a': { 'b': { 'c': 1 } } }")).Should().BeTrue();

        Comparer.Equals(
            JObject.Parse("{ 'a': { 'b': { 'c': 1 } } }"),
            JObject.Parse("{ 'a': { 'b': { 'c': 2 } } }")).Should().BeFalse();
    }

    [Fact]
    public void Equals_ForArrays_ComparesElementwise()
    {
        Comparer.Equals(
            JObject.Parse("{ 'a': [1, 2, 3] }"),
            JObject.Parse("{ 'a': [1, 2, 3] }")).Should().BeTrue();

        Comparer.Equals(
            JObject.Parse("{ 'a': [1, 2, 3] }"),
            JObject.Parse("{ 'a': [3, 2, 1] }")).Should().BeFalse();
    }

    [Fact]
    public void Equals_ForEmptyObjects_IsTrue()
        => Comparer.Equals(new JObject(), new JObject()).Should().BeTrue();

    [Fact]
    public void Equals_ForBothNull_IsTrue()
        => Comparer.Equals(null, null).Should().BeTrue();

    [Fact]
    public void Equals_ForOneNull_IsFalse()
    {
        Comparer.Equals(JObject.Parse("{ 'a': 1 }"), null).Should().BeFalse();
        Comparer.Equals(null, JObject.Parse("{ 'a': 1 }")).Should().BeFalse();
    }

    [Fact]
    public void Equals_ForTheSameReference_IsTrue()
    {
        var instance = JObject.Parse("{ 'a': 1 }");
        Comparer.Equals(instance, instance).Should().BeTrue();
    }

    [Fact]
    public void Equals_DistinguishesNullFromAbsent()
        => Comparer.Equals(JObject.Parse("{ 'a': null }"), new JObject()).Should().BeFalse();

    [Fact]
    public void Register_MakesTheComparerDiscoverableThroughTheRegistry()
    {
        JObjectValueComparer.Register();

        // TryRegister is idempotent (TryAdd), so calling it twice must not throw. This
        // also covers the case where something else registered it first.
        JObjectValueComparer.Register();

        ComparerRegistry.TryGet<JObject>(out var registered).Should().BeTrue();
        registered.Should().BeOfType<JObjectValueComparer>();
    }

    [Fact]
    public void For_ReturnsTheRegisteredComparer_AfterRegistration()
    {
        JObjectValueComparer.Register();

        ComparerRegistry.For<JObject>().Should().BeOfType<JObjectValueComparer>();
    }

    [Fact]
    public void GetHashCode_ForEqualObjects_Matches()
    {
        var left = JObject.Parse("{ 'a': 1 }");
        var right = JObject.Parse("{ 'a': 1 }");

        // Equal instances must not disagree on their hash, or a dictionary keyed on this
        // comparer would lose entries.
        Comparer.GetHashCode(right).Should().Be(Comparer.GetHashCode(left));
    }
}
