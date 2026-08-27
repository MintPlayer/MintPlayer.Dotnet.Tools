namespace MintPlayer.Assertions.Tests;

public class DictionaryAssertionsTests
{
    private static readonly Dictionary<string, int> Ages = new()
    {
        ["alice"] = 30,
        ["bob"] = 25,
    };

    private static AssertionFailedException Fails(Action action)
    {
        var exception = Record.Exception(action);
        return Assert.IsType<AssertionFailedException>(exception);
    }

    [Fact]
    public void BeEmpty()
    {
        new Dictionary<string, int>().Should().BeEmpty();

        var ex = Fails(() => Ages.Should().BeEmpty());
        Assert.Contains("to be empty", ex.Message);
    }

    [Fact]
    public void BeEmpty_NullSubject()
    {
        Dictionary<string, int>? subject = null;
        var ex = Fails(() => subject.Should().BeEmpty());
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotBeEmpty()
    {
        Ages.Should().NotBeEmpty();

        var ex = Fails(() => new Dictionary<string, int>().Should().NotBeEmpty());
        Assert.Contains("not to be empty", ex.Message);
    }

    [Fact]
    public void HaveCount()
    {
        Ages.Should().HaveCount(2);

        var ex = Fails(() => Ages.Should().HaveCount(3));
        Assert.Contains("to contain 3 item(s)", ex.Message);
        Assert.Contains("but found 2", ex.Message);
    }

    [Fact]
    public void ContainKey()
    {
        var which = Ages.Should().ContainKey("alice").Which;
        Assert.Equal(30, which);

        var ex = Fails(() => Ages.Should().ContainKey("carol"));
        Assert.Contains("to contain key \"carol\"", ex.Message);
    }

    [Fact]
    public void ContainKeys()
    {
        Ages.Should().ContainKeys("alice", "bob");

        var ex = Fails(() => Ages.Should().ContainKeys("alice", "carol", "dave"));
        Assert.Contains("to contain keys", ex.Message);
        Assert.Contains("could not find key(s) {\"carol\", \"dave\"}", ex.Message);
    }

    [Fact]
    public void NotContainKey()
    {
        Ages.Should().NotContainKey("carol");

        var ex = Fails(() => Ages.Should().NotContainKey("alice"));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("key \"alice\"", ex.Message);
    }

    [Fact]
    public void ContainValue()
    {
        Ages.Should().ContainValue(30);

        var ex = Fails(() => Ages.Should().ContainValue(99));
        Assert.Contains("to contain value 99", ex.Message);
    }

    [Fact]
    public void ContainValues()
    {
        Ages.Should().ContainValues(30, 25);

        var ex = Fails(() => Ages.Should().ContainValues(30, 99));
        Assert.Contains("could not find value(s) {99}", ex.Message);
    }

    [Fact]
    public void NotContainValue()
    {
        Ages.Should().NotContainValue(99);

        var ex = Fails(() => Ages.Should().NotContainValue(30));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("value 30", ex.Message);
    }

    [Fact]
    public void Contain_KeyAndValue()
    {
        Ages.Should().Contain("alice", 30);

        var missingKey = Fails(() => Ages.Should().Contain("carol", 30));
        Assert.Contains("but the key was not found", missingKey.Message);

        var wrongValue = Fails(() => Ages.Should().Contain("alice", 31));
        Assert.Contains("to contain 31 at key \"alice\"", wrongValue.Message);
        Assert.Contains("but found 30", wrongValue.Message);
    }

    [Fact]
    public void Contain_Pair()
    {
        Ages.Should().Contain(new KeyValuePair<string, int>("bob", 25));

        var ex = Fails(() => Ages.Should().Contain(new KeyValuePair<string, int>("bob", 26)));
        Assert.Contains("to contain 26 at key \"bob\"", ex.Message);
    }

    [Fact]
    public void NotContain_KeyAndValue()
    {
        Ages.Should().NotContain("alice", 31);
        Ages.Should().NotContain("carol", 30);

        var ex = Fails(() => Ages.Should().NotContain("alice", 30));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("30 at key \"alice\"", ex.Message);
    }

    [Fact]
    public void Should_PicksDictionaryAssertions_ForPairSequences()
    {
        // A plain sequence of pairs (not a dictionary) also binds to the dictionary assertions.
        var pairs = new List<KeyValuePair<string, int>> { new("x", 1) };
        Assert.Equal(1, pairs.Should().ContainKey("x").Which);
    }
}
