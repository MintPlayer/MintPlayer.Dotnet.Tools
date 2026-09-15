namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The collection and dictionary surface added in Phase 2 (PRD §3).
/// </summary>
public class Phase2CollectionAdditionsTests
{
    private static readonly int[] Numbers = [1, 2, 3, 4, 5];

    #region Bulk membership

    [Fact]
    public void Contain_TakesSeveralExpectedItems()
    {
        Numbers.Should().Contain([2, 4]);

        var ex = Record.Exception(() => Numbers.Should().Contain([2, 9, 11]));
        Assert.IsType<AssertionFailedException>(ex);
    }

    /// <summary>
    /// The reason the bulk overload exists: the message names <b>every</b> missing item, where one
    /// call per item stops at the first failure.
    /// </summary>
    [Fact]
    public void Contain_NamesEveryMissingItem()
    {
        var ex = Record.Exception(() => Numbers.Should().Contain([9, 11]));

        Assert.Contains("9", ex!.Message);
        Assert.Contains("11", ex.Message);
    }

    [Fact]
    public void NotContain_TakesSeveralItems()
    {
        Numbers.Should().NotContain([9, 11]);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => Numbers.Should().NotContain([9, 3])));
    }

    #endregion

    #region Set relations

    [Fact]
    public void BeSupersetOf()
    {
        Numbers.Should().BeSupersetOf([1, 3, 5]);
        Numbers.Should().NotBeSupersetOf([1, 99]);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => Numbers.Should().BeSupersetOf([1, 99])));
    }

    #endregion

    #region Positional

    [Fact]
    public void HaveElementAt_ChecksThePositionAndExposesTheItem()
    {
        Numbers.Should().HaveElementAt(2, 3).Which.Should().Be(3);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => Numbers.Should().HaveElementAt(2, 99)));
    }

    [Fact]
    public void HaveElementAt_Fails_WhenTheIndexIsOutOfRange()
    {
        var ex = Record.Exception(() => Numbers.Should().HaveElementAt(99, 1));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("only 5 item(s)", ex.Message);
    }

    [Fact]
    public void HaveElementPrecedingAndSucceeding()
    {
        Numbers.Should().HaveElementPreceding(3, 2);
        Numbers.Should().HaveElementSucceeding(3, 4);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => Numbers.Should().HaveElementPreceding(3, 99)));
    }

    /// <summary>The first item has no predecessor, so the assertion must fail rather than wrap around.</summary>
    [Fact]
    public void HaveElementPreceding_Fails_ForTheFirstItem()
        => Assert.IsType<AssertionFailedException>(
            Record.Exception(() => Numbers.Should().HaveElementPreceding(1, 5)));

    #endregion

    #region Types and projections

    [Fact]
    public void ContainItemsAssignableTo()
    {
        object[] mixed = ["a", "b"];
        mixed.Should().ContainItemsAssignableTo<string>();

        object[] withAnInt = ["a", 1];
        var ex = Record.Exception(() => withAnInt.Should().ContainItemsAssignableTo<string>());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("index 1", ex.Message);
    }

    [Fact]
    public void OnlyHaveUniqueItems_ByKey()
    {
        var people = new[] { new Person(1, "Ann"), new Person(2, "Bob") };
        people.Should().OnlyHaveUniqueItems(p => p.Id);

        var clashing = new[] { new Person(1, "Ann"), new Person(1, "Bob") };
        var ex = Record.Exception(() => clashing.Should().OnlyHaveUniqueItems(p => p.Id));
        Assert.IsType<AssertionFailedException>(ex);
    }

    /// <summary>
    /// Uniqueness by projection is the useful form: these two items are distinct references, so the
    /// no-argument overload passes while the Id is duplicated.
    /// </summary>
    [Fact]
    public void OnlyHaveUniqueItems_ByKey_CatchesWhatTheItemLevelOverloadCannot()
    {
        var clashing = new[] { new Person(1, "Ann"), new Person(1, "Bob") };

        clashing.Should().OnlyHaveUniqueItems();
        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => clashing.Should().OnlyHaveUniqueItems(p => p.Id)));
    }

    [Fact]
    public void NotContainNulls_ByKey()
    {
        var people = new[] { new Person(1, "Ann") };
        people.Should().NotContainNulls(p => p.Name);

        var nameless = new[] { new Person(1, null!) };
        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => nameless.Should().NotContainNulls(p => p.Name)));
    }

    private sealed class Person(int id, string name)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
    }

    #endregion

    #region Dictionaries

    private static Dictionary<string, int> Ages() => new() { ["ann"] = 30, ["bob"] = 40 };

    [Fact]
    public void Equal_IsOrderInsensitive()
    {
        Ages().Should().Equal(new Dictionary<string, int> { ["bob"] = 40, ["ann"] = 30 });

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => Ages().Should().Equal(new Dictionary<string, int> { ["ann"] = 30 })));
    }

    [Fact]
    public void Equal_Fails_WhenAValueDiffers()
    {
        var ex = Record.Exception(() =>
            Ages().Should().Equal(new Dictionary<string, int> { ["ann"] = 30, ["bob"] = 99 }));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("bob", ex.Message);
    }

    [Fact]
    public void NotEqual()
        => Ages().Should().NotEqual(new Dictionary<string, int> { ["ann"] = 30 });

    /// <summary>
    /// Bulk pair assertions take a collection, deliberately — there is no <c>params</c> overload,
    /// because one would silently hijack single-pair calls (see the remarks on the method).
    /// </summary>
    [Fact]
    public void Contain_TakesSeveralPairs()
    {
        KeyValuePair<string, int>[] both = [new("ann", 30), new("bob", 40)];
        Ages().Should().Contain(both);

        KeyValuePair<string, int>[] wrong = [new("ann", 99)];
        Assert.IsType<AssertionFailedException>(Record.Exception(() => Ages().Should().Contain(wrong)));
    }

    /// <summary>A single pair still reaches the single-pair overload, with its own message.</summary>
    [Fact]
    public void ASinglePairStillBindsToTheSinglePairOverload()
    {
        var ex = Record.Exception(() => Ages().Should().Contain(new KeyValuePair<string, int>("ann", 99)));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("at key", ex.Message);
    }

    [Fact]
    public void NotContain_TakesSeveralPairs()
    {
        KeyValuePair<string, int>[] neither = [new("ann", 99), new("zoe", 1)];
        Ages().Should().NotContain(neither);
    }

    [Fact]
    public void TheCountFamily()
    {
        Ages().Should()
            .HaveCountGreaterThan(1).And
            .HaveCountGreaterThanOrEqualTo(2).And
            .HaveCountLessThan(3).And
            .HaveCountLessThanOrEqualTo(2);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => Ages().Should().HaveCountGreaterThan(5)));
    }

    /// <summary>
    /// Key lookups go through the dictionary's own comparer, so a case-insensitive dictionary finds
    /// a key the default comparer would call missing. The new bulk overloads must honour that too.
    /// </summary>
    [Fact]
    public void BulkContain_HonoursTheDictionarysOwnComparer()
    {
        var caseInsensitive = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Ann"] = 30 };

        caseInsensitive.Should().Contain(new KeyValuePair<string, int>("ANN", 30));
    }

    #endregion
}
