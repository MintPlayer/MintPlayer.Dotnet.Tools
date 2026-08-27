using MintPlayer.StringBuilder.Extensions;

namespace MintPlayer.StringBuilder.Extensions.Tests;

public class SplitLinesTests
{
    private static List<string> Lines(string input)
    {
        var lines = new List<string>();
        foreach (var entry in input.SplitLines())
            lines.Add(entry.Line.ToString());
        return lines;
    }

    [Fact]
    public void SplitLines_SplitsOnLf()
        => Lines("a\nb\nc").Should().Equal(["a", "b", "c"]);

    [Fact]
    public void SplitLines_SplitsOnCrLf()
        => Lines("a\r\nb").Should().Equal(["a", "b"]);

    [Fact]
    public void SplitLines_SplitsOnALoneCr()
        => Lines("a\rb").Should().Equal(["a", "b"]);

    [Fact]
    public void SplitLines_HandlesMixedEndings()
        => Lines("a\r\nb\nc\rd").Should().Equal(["a", "b", "c", "d"]);

    [Fact]
    public void SplitLines_OnASingleLine_YieldsThatLine()
        => Lines("only").Should().Equal(["only"]);

    [Fact]
    public void SplitLines_OnEmptyInput_YieldsNothing()
        => Lines(string.Empty).Should().BeEmpty();

    [Fact]
    public void SplitLines_KeepsEmptyLines()
        => Lines("a\n\nb").Should().Equal(["a", string.Empty, "b"]);

    [Fact]
    public void SplitLines_ReportsTheSeparatorItConsumed()
    {
        var separators = new List<string>();
        foreach (var entry in "a\r\nb\nc".SplitLines())
            separators.Add(entry.Separator.ToString());

        separators.Should().Equal(["\r\n", "\n", string.Empty]);
    }

    [Fact]
    public void SplitLines_SupportsDeconstruction()
    {
        var pairs = new List<(string Line, string Separator)>();
        foreach (var (line, separator) in "a\nb".SplitLines())
            pairs.Add((line.ToString(), separator.ToString()));

        pairs.Should().Equal([("a", "\n"), ("b", string.Empty)]);
    }

    [Fact]
    public void SplitLines_SupportsImplicitConversionToSpan()
    {
        var lines = new List<string>();
        foreach (ReadOnlySpan<char> line in "a\nb".SplitLines())
            lines.Add(line.ToString());

        lines.Should().Equal(["a", "b"]);
    }

    [Fact]
    public void SplitLines_WithATrailingCrAtTheVeryEnd_TreatsItAsASeparator()
    {
        // A '\r' as the final character cannot consume a following '\n', so it falls through
        // to the single-character separator branch.
        Lines("a\r").Should().Equal(["a"]);
    }
}

public class DedentTests
{
    private static readonly string NL = Environment.NewLine;

    [Fact]
    public void Dedent_RemovesTheClosingIndentFromEveryLine()
    {
        var input = "class Foo" + NL + "    line1" + NL + "    line2" + NL + "    ";

        var result = input.Dedent();

        result.Should().Be("class Foo" + NL + "line1" + NL + "line2" + NL + string.Empty);
    }

    [Fact]
    public void Dedent_LeavesTheFirstLineUntouched()
    {
        var input = "unindented" + NL + "  indented" + NL + "  ";

        input.Dedent().Should().StartWith("unindented");
    }

    [Fact]
    public void Dedent_KeepsExtraIndentationBeyondTheBaseline()
    {
        var input = "root" + NL + "  two" + NL + "    four" + NL + "  ";

        input.Dedent().Should().Be("root" + NL + "two" + NL + "  four" + NL + string.Empty);
    }

    [Fact]
    public void Dedent_TreatsATabAsFourSpaces()
    {
        var input = "root" + NL + "\tline" + NL + "    ";

        input.Dedent().Should().Be("root" + NL + "line" + NL + string.Empty);
    }

    [Fact]
    public void Dedent_WithNoIndentation_IsANoOp()
    {
        var input = "a" + NL + "b" + NL;

        input.Dedent().Should().Be(input);
    }

    [Fact]
    public void Dedent_OnAnEmptyString_IsEmpty()
        => string.Empty.Dedent().Should().Be(string.Empty);

    [Fact]
    public void Dedent_WhenTheLastLineHasNonWhitespace_Throws()
    {
        var input = "root" + NL + "  line" + NL + "  not-whitespace";

        var act = () => input.Dedent();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Dedent_WhenALineIsShorterThanTheBaseline_Throws()
    {
        var input = "root" + NL + "  short" + NL + "        ";

        var act = () => input.Dedent();

        act.Should().Throw<Exception>().WithMessage("*too few spaces*");
    }

    [Fact]
    public void Dedent_OnAWhollyBlankLine_ProducesAnEmptyLine()
    {
        var input = "root" + NL + "  a" + NL + string.Empty + NL + "  ";

        // A blank line runs out of characters before reaching the baseline, so DedentLine
        // returns string.Empty rather than throwing.
        input.Dedent().Should().Be("root" + NL + "a" + NL + string.Empty + NL + string.Empty);
    }
}
