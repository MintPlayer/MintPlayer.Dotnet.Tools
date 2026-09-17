namespace MintPlayer.Assertions.Tests;

/// <summary>
/// Pins the behaviour of unordered <c>BeEquivalentTo</c> matching on collections.
/// </summary>
/// <remarks>
/// These tests exist because the walker used to match greedily — each expectation claimed the first
/// unmatched subject item it was equivalent to — and greedy first-fit is not a maximum matching. It
/// reported a difference between two collections that were genuinely equivalent, whenever the item
/// it claimed was the only candidate a later expectation had.
/// <para>
/// Reproducing that needs a NON-TRANSITIVE equivalence between items, which is why these collections
/// are <c>object[]</c>. Comparison is driven by the expectation's members, so when every item on both
/// sides has the same type the relation is transitive, the candidate graph is a disjoint union of
/// complete blocks, and greedy is accidentally optimal. Mixed expectation types are what make one
/// expectation strictly pickier than another about the same subject item. Do not "tidy" these to a
/// typed array — the tests would still pass, and they would stop testing anything.
/// </para>
/// </remarks>
public class UnorderedCollectionMatchingTests
{
    /// <summary>Compared on Name alone: equivalent to any subject item whose Name matches.</summary>
    private sealed class ByName
    {
        public string Name { get; set; } = "";
    }

    /// <summary>Compared on Name and Size: strictly pickier than <see cref="ByName"/>.</summary>
    private sealed class ByNameAndSize
    {
        public string Name { get; set; } = "";
        public int Size { get; set; }
    }

    private sealed class Item
    {
        public string Name { get; set; } = "";
        public int Size { get; set; }
    }

    [Fact]
    public void AMatchExistsEvenWhenTheFirstFitStrandsALaterExpectation()
    {
        // ByName is equivalent to both subject items; ByNameAndSize only to the first.
        // Greedy hands the first item to ByName and strands ByNameAndSize; the perfect matching
        // (ByName -> second, ByNameAndSize -> first) exists and must be found.
        object[] subject = [new Item { Name = "a", Size = 1 }, new Item { Name = "a", Size = 2 }];
        object[] expectation = [new ByName { Name = "a" }, new ByNameAndSize { Name = "a", Size = 1 }];

        subject.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void TheSameMatchIsFoundWhenTheStrandedExpectationComesFirst()
    {
        // The order the greedy pre-pass happens to walk in must not change the answer.
        object[] subject = [new Item { Name = "a", Size = 1 }, new Item { Name = "a", Size = 2 }];
        object[] expectation = [new ByNameAndSize { Name = "a", Size = 1 }, new ByName { Name = "a" }];

        subject.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void AChainOfDisplacementsIsResolved()
    {
        // Three expectations of decreasing pickiness against three items. Placing the loosest one
        // first takes a subject each of the other two needs, so the fix has to displace more than
        // one incumbent to succeed — a single re-try would not be enough.
        object[] subject =
        [
            new Item { Name = "a", Size = 1 },
            new Item { Name = "a", Size = 2 },
            new Item { Name = "a", Size = 3 },
        ];
        object[] expectation =
        [
            new ByName { Name = "a" },
            new ByNameAndSize { Name = "a", Size = 1 },
            new ByNameAndSize { Name = "a", Size = 2 },
        ];

        subject.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void NoMatchIsStillReportedAsADifference()
    {
        // The fix must not turn into "find an excuse to pass". There is no assignment here that
        // satisfies both expectations, and the assertion has to say so.
        object[] subject = [new Item { Name = "a", Size = 1 }, new Item { Name = "a", Size = 2 }];
        object[] expectation = [new ByNameAndSize { Name = "a", Size = 1 }, new ByNameAndSize { Name = "a", Size = 9 }];

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("no equivalent item was found", ex.Message);
    }

    [Fact]
    public void LeftoverSubjectItemsAreStillReported()
    {
        object[] subject = [new Item { Name = "a", Size = 1 }, new Item { Name = "b", Size = 2 }];
        object[] expectation = [new ByName { Name = "a" }];

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("unexpected item(s)", ex.Message);
    }

    [Fact]
    public void AnEmptySubjectReportsEveryExpectation()
    {
        object[] subject = [];
        object[] expectation = [new ByName { Name = "a" }, new ByName { Name = "b" }];

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("no equivalent item was found", ex.Message);
    }

    [Fact]
    public void StrictOrderingIsUnaffected()
    {
        // The matcher is skipped entirely under strict ordering; this pins that it stays skipped.
        Item[] subject = [new() { Name = "a", Size = 1 }, new() { Name = "b", Size = 2 }];
        Item[] expectation = [new() { Name = "a", Size = 1 }, new() { Name = "b", Size = 2 }];

        subject.Should().BeEquivalentTo(expectation, o => o.WithStrictOrdering());

        Item[] reversed = [expectation[1], expectation[0]];
        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(reversed, o => o.WithStrictOrdering()));
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void ValueLikeCollectionsStillTakeTheMultisetPath()
    {
        // Not a matching test: it guards the shortcut that keeps the matcher off collections of
        // primitives entirely. If this starts failing, the O(n) multiset path has been lost.
        int[] subject = [3, 1, 2, 1];
        int[] expectation = [1, 1, 2, 3];

        subject.Should().BeEquivalentTo(expectation);

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo([1, 2, 3, 3]));
        Assert.IsType<AssertionFailedException>(ex);
    }
}
