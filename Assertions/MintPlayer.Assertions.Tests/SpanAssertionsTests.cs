namespace MintPlayer.Assertions.Tests;

public class SpanAssertionsTests
{
    private static AssertionFailedException Fails(Action action)
    {
        var exception = Record.Exception(action);
        return Assert.IsType<AssertionFailedException>(exception);
    }

    private static Span<int> Span(params int[] items) => items.AsSpan();

    private static ReadOnlySpan<int> ReadOnlySpan(params int[] items) => items.AsSpan();

    [Fact]
    public void Be()
    {
        Span(1, 2, 3).Should().Be(ReadOnlySpan(1, 2, 3));
        ReadOnlySpan(1, 2, 3).Should().Be(ReadOnlySpan(1, 2, 3));

        var ex = Fails(() => Span(1, 2).Should().Be(ReadOnlySpan(1, 3)));
        Assert.Contains("to be {1, 3}", ex.Message);
        Assert.Contains("but found {1, 2}", ex.Message);
    }

    [Fact]
    public void Equal()
    {
        Span(1, 2).Should().Equal(ReadOnlySpan(1, 2));
        ReadOnlySpan(1, 2).Should().Equal(ReadOnlySpan(1, 2));

        var ex = Fails(() => ReadOnlySpan(1, 2).Should().Equal(ReadOnlySpan(1, 2, 3)));
        Assert.Contains("to equal {1, 2, 3}", ex.Message);
    }

    [Fact]
    public void HaveLength()
    {
        Span(1, 2, 3).Should().HaveLength(3);
        ReadOnlySpan(1, 2, 3).Should().HaveLength(3);

        var ex = Fails(() => Span(1, 2).Should().HaveLength(3));
        Assert.Contains("to have length 3", ex.Message);
        Assert.Contains("but found 2", ex.Message);
    }

    [Fact]
    public void BeEmpty()
    {
        Span().Should().BeEmpty();
        ReadOnlySpan().Should().BeEmpty();

        var ex = Fails(() => Span(1).Should().BeEmpty());
        Assert.Contains("to be empty", ex.Message);
        Assert.Contains("{1}", ex.Message);
    }

    [Fact]
    public void NotBeEmpty()
    {
        Span(1).Should().NotBeEmpty();
        ReadOnlySpan(1).Should().NotBeEmpty();

        var ex = Fails(() => ReadOnlySpan().Should().NotBeEmpty());
        Assert.Contains("not to be empty", ex.Message);
    }

    [Fact]
    public void Contain()
    {
        Span(1, 2, 3).Should().Contain(2);
        ReadOnlySpan(1, 2, 3).Should().Contain(2);

        var ex = Fails(() => Span(1, 3).Should().Contain(2));
        Assert.Contains("to contain 2", ex.Message);
        Assert.Contains("{1, 3}", ex.Message);
    }

    [Fact]
    public void StartWith()
    {
        Span(1, 2, 3).Should().StartWith(ReadOnlySpan(1, 2));
        ReadOnlySpan(1, 2, 3).Should().StartWith(ReadOnlySpan(1, 2));

        var ex = Fails(() => Span(1, 2, 3).Should().StartWith(ReadOnlySpan(2, 3)));
        Assert.Contains("to start with {2, 3}", ex.Message);
    }

    [Fact]
    public void EndWith()
    {
        Span(1, 2, 3).Should().EndWith(ReadOnlySpan(2, 3));
        ReadOnlySpan(1, 2, 3).Should().EndWith(ReadOnlySpan(2, 3));

        var ex = Fails(() => ReadOnlySpan(1, 2, 3).Should().EndWith(ReadOnlySpan(1, 2)));
        Assert.Contains("to end with {1, 2}", ex.Message);
    }

    [Fact]
    public void Chaining_ReturnsTheSameSpan()
    {
        Span(1, 2, 3).Should().HaveLength(3).And.Contain(2).And.StartWith(ReadOnlySpan(1));
    }

    [Fact]
    public void Because_IsWovenIntoTheMessage()
    {
        var ex = Fails(() => Span(1).Should().HaveLength(2, "the buffer holds {0} entries", 2));
        Assert.Contains("because the buffer holds 2 entries", ex.Message);
    }
}
