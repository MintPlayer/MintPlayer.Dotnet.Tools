using MintPlayer.EnumerableExtensions;

namespace MintPlayer.EnumerableExtensions.Tests;

/// <summary>
/// RandomElement uses the shared Random, so these assert membership and distribution
/// properties — never a specific value, which would be a flaky test.
/// </summary>
public class RandomEntryTests
{
    [Fact]
    public void RandomElement_ReturnsAnElementOfTheSource()
    {
        var source = new[] { 1, 2, 3, 4, 5 };

        for (var i = 0; i < 100; i++)
            source.Should().Contain(source.RandomElement());
    }

    [Fact]
    public void RandomElement_OnSingleElement_AlwaysReturnsIt()
        => new[] { 7 }.RandomElement().Should().Be(7);

    [Fact]
    public void RandomElement_OnEmpty_Throws()
    {
        var act = () => Array.Empty<int>().RandomElement();
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RandomElement_EnumeratesALazySourceOnlyOnce()
    {
        var enumerations = 0;

        IEnumerable<int> Tracked()
        {
            enumerations++;
            yield return 1;
            yield return 2;
        }

        Tracked().RandomElement();
        enumerations.Should().Be(1);
    }

    [Fact]
    public void RandomElement_OverManyDraws_ReachesEveryElement()
    {
        var source = new[] { 1, 2, 3 };
        var seen = new HashSet<int>();

        // 300 draws over 3 elements: the chance of missing one is (2/3)^300, which is
        // far beyond negligible. This asserts the index is not pinned to one position.
        for (var i = 0; i < 300; i++)
            seen.Add(source.RandomElement());

        seen.Should().HaveCount(3);
    }
}
