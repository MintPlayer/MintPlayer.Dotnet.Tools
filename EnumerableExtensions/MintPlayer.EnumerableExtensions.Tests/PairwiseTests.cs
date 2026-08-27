using MintPlayer.EnumerableExtensions;

namespace MintPlayer.EnumerableExtensions.Tests;

public class PairwiseTests
{
    [Fact]
    public void Pairwise_OnEvenCount_PairsEveryElement()
    {
        var result = new[] { 1, 2, 3, 4, 5, 6 }.Pairwise().ToList();

        result.Should().HaveCount(3);
        result[0].Should().Be(Tuple.Create(1, 2));
        result[1].Should().Be(Tuple.Create(3, 4));
        result[2].Should().Be(Tuple.Create(5, 6));
    }

    /// <summary>
    /// The declared return type is Tuple&lt;T, T?&gt;, but T is unconstrained, so for a value
    /// type T the <c>?</c> is only a nullable *annotation* — the runtime type is
    /// Tuple&lt;int, int&gt;, not Tuple&lt;int, int?&gt;, and the missing partner is 0 rather than
    /// null. Pinned because it is easy to read the signature the other way, and because
    /// changing it would be a breaking API change.
    /// </summary>
    [Fact]
    public void Pairwise_OverValueTypes_YieldsANonNullableSecondItem()
    {
        var result = new[] { 1, 2 }.Pairwise().ToList();

        result[0].Should().BeOfType<Tuple<int, int>>();
    }

    [Fact]
    public void Pairwise_OnOddCount_LeavesTheLastPartnerAtDefault()
    {
        var result = new[] { 1, 2, 3 }.Pairwise().ToList();

        result.Should().HaveCount(2);
        result[0].Should().Be(Tuple.Create(1, 2));
        result[1].Item1.Should().Be(3);
        result[1].Item2.Should().Be(0);
    }

    [Fact]
    public void Pairwise_OnSingleElement_ReturnsOneHalfPair()
    {
        var result = new[] { 42 }.Pairwise().ToList();

        result.Should().HaveCount(1);
        result[0].Item1.Should().Be(42);
    }

    [Fact]
    public void Pairwise_OnEmpty_ReturnsEmpty()
        => Array.Empty<int>().Pairwise().Should().BeEmpty();

    [Fact]
    public void Pairwise_OnReferenceTypes_LeavesTheLastPartnerNull()
    {
        var result = new[] { "a", "b", "c" }.Pairwise().ToList();

        result.Should().HaveCount(2);
        result[1].Item1.Should().Be("c");
        result[1].Item2.Should().BeNull();
    }

    [Fact]
    public void Pairwise_OnNull_Throws()
    {
        IEnumerable<int> source = null!;
        var act = () => source.Pairwise().ToList();
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Regression for D1 in docs/PRD-TestCoverage.md. The previous implementation called
    /// Count() and then ElementAt(index + 1) on the source, enumerating it three times.
    /// A sequence that can only be walked once — an iterator over a reader, a network
    /// stream — silently produced wrong pairs.
    /// </summary>
    [Fact]
    public void Pairwise_EnumeratesTheSourceExactlyOnce()
    {
        var enumerations = 0;

        IEnumerable<int> OneShot()
        {
            enumerations++;
            yield return 1;
            yield return 2;
            yield return 3;
            yield return 4;
        }

        var result = OneShot().Pairwise().ToList();

        enumerations.Should().Be(1);
        result.Should().HaveCount(2);
        result[0].Should().Be(Tuple.Create(1, 2));
        result[1].Should().Be(Tuple.Create(3, 4));
    }

    [Fact]
    public void Pairwise_IsLazy_AndDoesNotThrowBeforeEnumeration()
    {
        var started = false;

        IEnumerable<int> Tracked()
        {
            started = true;
            yield return 1;
        }

        var query = Tracked().Pairwise();
        started.Should().BeFalse();

        query.ToList();
        started.Should().BeTrue();
    }
}
