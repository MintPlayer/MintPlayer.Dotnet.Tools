namespace MintPlayer.StringExtensions.Tests;

public class FormatAdvancedTests
{
    [Fact]
    public void FormatAdvanced_WithoutAnchors_BehavesLikeStringFormat()
    {
        var result = StringExtensions.FormatAdvanced("Hello {0}, you are {1}", out var anchors, "Bob", 42);

        result.Should().Be("Hello Bob, you are 42");
        anchors.Should().HaveCount(2);
        anchors[0].Should().BeEmpty();
        anchors[1].Should().BeEmpty();
    }

    /// <summary>
    /// A SINGLE colon introduces a standard .NET format specifier, not an anchor — the
    /// anchor group only starts at the second colon. So "{0:Name}" is a format string
    /// (which String.Format then ignores for a string argument), and the anchor list is
    /// empty. Pinned because "{0:Name}" reads like an anchor and is not one.
    /// </summary>
    [Fact]
    public void FormatAdvanced_TreatsASingleColonAsAFormatSpecifier_NotAnAnchor()
    {
        var result = StringExtensions.FormatAdvanced("Hello {0:Name}", out var anchors, "Bob");

        result.Should().Be("Hello Bob");
        anchors.Should().HaveCount(1);
        anchors[0].Should().BeEmpty();
    }

    [Fact]
    public void FormatAdvanced_StripsAnchorsFromTheOutput_AndReportsThem()
    {
        // Two colons: no format, one anchor.
        var result = StringExtensions.FormatAdvanced("Hello {0::Name}", out var anchors, "Bob");

        result.Should().Be("Hello Bob");
        anchors.Should().HaveCount(1);
        anchors[0].Should().Equal(["Name"]);
    }

    [Fact]
    public void FormatAdvanced_KeepsANumericFormatSpecifier()
    {
        var result = StringExtensions.FormatAdvanced("{0:X4}", out var anchors, 255);

        result.Should().Be("00FF");
        anchors[0].Should().BeEmpty();
    }

    [Fact]
    public void FormatAdvanced_SupportsAFormatAndAnAnchorTogether()
    {
        var result = StringExtensions.FormatAdvanced("{0:X4:Hex.Value}", out var anchors, 255);

        result.Should().Be("00FF");
        anchors[0].Should().Equal(["Hex.Value"]);
    }

    [Fact]
    public void FormatAdvanced_GroupsAnchorsFromRepeatedPlaceholders()
    {
        var result = StringExtensions.FormatAdvanced("{0::First} and {0::Second}", out var anchors, "x");

        result.Should().Be("x and x");
        anchors.Should().HaveCount(1);
        anchors[0].Should().Equal(["First", "Second"]);
    }

    [Fact]
    public void FormatAdvanced_WithNoPlaceholders_ReturnsTheFormatUnchanged()
    {
        var result = StringExtensions.FormatAdvanced("nothing to substitute", out var anchors);

        result.Should().Be("nothing to substitute");
        anchors.Should().BeEmpty();
    }

    [Fact]
    public void FormatAdvanced_WithTooFewArguments_Throws()
    {
        var act = () => StringExtensions.FormatAdvanced("{0} {1}", out _, "only-one");
        act.Should().Throw<FormatException>();
    }
}
