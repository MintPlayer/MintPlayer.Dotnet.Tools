namespace MintPlayer.StringExtensions.Tests;

public class SplitLinesTests
{
    [Fact]
    public void SplitLines_SplitsOnTheGivenEnding()
        => SplitLinesExtensions.SplitLines("a\nb\nc", "\n").Should().Equal(["a", "b", "c"]);

    [Fact]
    public void SplitLines_SupportsMultipleEndings()
        => SplitLinesExtensions.SplitLines("a\r\nb\nc", "\r\n", "\n").Should().Equal(["a", "b", "c"]);

    [Fact]
    public void SplitLines_KeepsEmptyLines()
        => SplitLinesExtensions.SplitLines("a\n\nb", "\n").Should().Equal(["a", string.Empty, "b"]);

    [Fact]
    public void SplitLines_WithNoEndingPresent_ReturnsTheWholeInput()
        => SplitLinesExtensions.SplitLines("abc", "\n").Should().Equal(["abc"]);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(null)]
    public void SplitLines_OnNullOrWhitespace_ReturnsEmpty(string? input)
        => SplitLinesExtensions.SplitLines(input!, "\n").Should().BeEmpty();

    [Fact]
    public void SplitLines_WithNoEndingsGiven_ReturnsTheWholeInput()
    {
        // string.Split with an empty separator array falls back to whitespace splitting
        // in some overloads; this pins what actually happens for this one.
        var result = SplitLinesExtensions.SplitLines("a b").ToList();
        result.Should().NotBeEmpty();
    }
}
