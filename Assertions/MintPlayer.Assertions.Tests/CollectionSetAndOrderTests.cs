namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The M5b collection additions: set relations, positional lookups, consecutive order and
/// multi-key ordering.
/// </summary>
public class CollectionSetAndOrderTests
{
    private sealed record Person(string Team, string Name, int Age);

    // -------------------------------------------------------------------------------------------
    // Supersets and proper sub/supersets
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void BeSupersetOf_PassesWhenEveryExpectedItemIsPresent()
    {
        int[] subject = [1, 2, 3, 4];

        subject.Should().BeSupersetOf([2, 4]).And.HaveCount(4);
    }

    [Fact]
    public void BeSupersetOf_ReportsWhatIsMissing()
    {
        int[] subject = [1, 2];

        var ex = Record.Exception(() => subject.Should().BeSupersetOf([2, 9]));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("9", ex.Message);
    }

    [Fact]
    public void NotBeSupersetOf_PassesWhenSomethingIsMissing()
    {
        int[] subject = [1, 2];

        subject.Should().NotBeSupersetOf([2, 9]);
    }

    /// <summary>
    /// "Proper" is about distinct values, not item counts. Comparing counts is the obvious
    /// implementation and is wrong for any collection holding duplicates — this is that case.
    /// </summary>
    [Fact]
    public void BeProperSubsetOf_IgnoresDuplicatesWhenDecidingProperness()
    {
        int[] subject = [1, 1];

        subject.Should().BeProperSubsetOf([1, 2]);
    }

    [Fact]
    public void BeProperSubsetOf_FailsForAnEqualSet()
    {
        int[] subject = [1, 2];

        var ex = Record.Exception(() => subject.Should().BeProperSubsetOf([2, 1]));

        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void BeProperSupersetOf_PassesWhenStrictlyLarger()
    {
        int[] subject = [1, 2, 3];

        subject.Should().BeProperSupersetOf([1, 2]);
    }

    [Fact]
    public void BeProperSupersetOf_FailsForAnEqualSet()
    {
        int[] subject = [1, 2, 2];

        var ex = Record.Exception(() => subject.Should().BeProperSupersetOf([2, 1]));

        Assert.IsType<AssertionFailedException>(ex);
    }

    // -------------------------------------------------------------------------------------------
    // Positional
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void HaveElementAt_PassesAndExposesTheElement()
    {
        string[] subject = ["a", "b", "c"];

        subject.Should().HaveElementAt(1, "b").Which.Should().Be("b");
    }

    /// <summary>
    /// Indexing past the end is a failed assertion, not an ArgumentOutOfRangeException: the test is
    /// asserting something false about the collection, and an exception would report that as an
    /// error rather than a failure.
    /// </summary>
    [Fact]
    public void HaveElementAt_ReportsAnOutOfRangeIndexAsAFailure()
    {
        string[] subject = ["a"];

        var ex = Record.Exception(() => subject.Should().HaveElementAt(5, "a"));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("1 item(s)", ex.Message);
    }

    [Fact]
    public void HaveElementAt_ReportsAMismatch()
    {
        string[] subject = ["a", "b"];

        var ex = Record.Exception(() => subject.Should().HaveElementAt(1, "z"));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("found \"b\"", ex.Message);
    }

    [Fact]
    public void HaveElementPrecedingAndSucceeding()
    {
        string[] subject = ["a", "b", "c"];

        subject.Should().HaveElementPreceding("b", "a");
        subject.Should().HaveElementSucceeding("b", "c");
    }

    [Fact]
    public void HaveElementPreceding_FailsAtTheStartOfTheCollection()
    {
        string[] subject = ["a", "b"];

        var ex = Record.Exception(() => subject.Should().HaveElementPreceding("a", "z"));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("at the start of the collection", ex.Message);
    }

    [Fact]
    public void HaveElementSucceeding_FailsWhenTheAnchorIsAbsent()
    {
        string[] subject = ["a", "b"];

        var ex = Record.Exception(() => subject.Should().HaveElementSucceeding("q", "z"));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("predecessor was not found", ex.Message);
    }

    // -------------------------------------------------------------------------------------------
    // Consecutive order
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The distinction from ContainInOrder is the whole reason both exist, so it is asserted rather
    /// than described: [1, 9, 2] contains [1, 2] in order but not consecutively.
    /// </summary>
    [Fact]
    public void ConsecutiveOrderIsStricterThanContainInOrder()
    {
        int[] subject = [1, 9, 2];

        subject.Should().ContainInOrder(1, 2);
        subject.Should().NotContainInConsecutiveOrder([1, 2]);
    }

    [Fact]
    public void ContainInConsecutiveOrder_PassesForAnAdjacentRun()
    {
        int[] subject = [0, 1, 2, 3];

        subject.Should().ContainInConsecutiveOrder([1, 2, 3]);
    }

    [Fact]
    public void NotContainInConsecutiveOrder_ReportsWhereTheRunWas()
    {
        int[] subject = [0, 1, 2, 3];

        var ex = Record.Exception(() => subject.Should().NotContainInConsecutiveOrder([1, 2]));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("index 1", ex.Message);
    }

    [Fact]
    public void AnEmptyRunMatchesAnything()
    {
        int[] subject = [1];

        subject.Should().ContainInConsecutiveOrder([]);
    }

    [Fact]
    public void ARunLongerThanTheCollectionIsNotFound()
    {
        int[] subject = [1, 2];

        subject.Should().NotContainInConsecutiveOrder([1, 2, 3]);
    }

    // -------------------------------------------------------------------------------------------
    // Multi-key ordering
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void BeOrderedBy_ThenBy_PassesWhenTiesAreOrdered()
    {
        Person[] subject =
        [
            new("a", "Ann", 30),
            new("a", "Bob", 40),
            new("b", "Cid", 20),
        ];

        subject.Should().BeOrderedBy(p => p.Team).ThenBeInAscendingOrder(p => p.Name).And.HaveCount(3);
    }

    /// <summary>
    /// The first key alone is satisfied here; only the tie-break makes it fail. If Then… were
    /// checking anything other than the composite ordering, this would pass.
    /// </summary>
    [Fact]
    public void BeOrderedBy_ThenBy_FailsWhenOnlyTheTieIsOutOfOrder()
    {
        Person[] subject =
        [
            new("a", "Bob", 40),
            new("a", "Ann", 30),
        ];

        subject.Should().BeOrderedBy(p => p.Team);

        var ex = Record.Exception(() =>
            subject.Should().BeOrderedBy(p => p.Team).ThenBeInAscendingOrder(p => p.Name));

        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void EachLevelCarriesItsOwnDirection()
    {
        Person[] subject =
        [
            new("b", "Ann", 30),
            new("a", "Zoe", 20),
            new("a", "Ann", 10),
        ];

        subject.Should().BeOrderedByDescending(p => p.Team).ThenBeInDescendingOrder(p => p.Name);
    }

    [Fact]
    public void ThreeKeysChain()
    {
        Person[] subject =
        [
            new("a", "Ann", 10),
            new("a", "Ann", 20),
            new("a", "Bob", 5),
        ];

        subject.Should()
            .BeOrderedBy(p => p.Team)
            .ThenBeInAscendingOrder(p => p.Name)
            .ThenBeInAscendingOrder(p => p.Age);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        int[] subject = [1];

        Assert.Throws<ArgumentNullException>(() => subject.Should().BeSupersetOf(null!));
        Assert.Throws<ArgumentNullException>(() => subject.Should().NotBeSupersetOf(null!));
        Assert.Throws<ArgumentNullException>(() => subject.Should().BeProperSubsetOf(null!));
        Assert.Throws<ArgumentNullException>(() => subject.Should().BeProperSupersetOf(null!));
        Assert.Throws<ArgumentNullException>(() => subject.Should().ContainInConsecutiveOrder(null!));
        Assert.Throws<ArgumentNullException>(() => subject.Should().NotContainInConsecutiveOrder(null!));
        Assert.Throws<ArgumentNullException>(() => subject.Should().BeOrderedBy<int>(null!));
    }
}
