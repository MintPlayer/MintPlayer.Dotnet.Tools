using MintPlayer.StringBuilder.Extensions;
using MintPlayer.StringBuilder.Extensions.Exceptions;
using SB = System.Text.StringBuilder;

namespace MintPlayer.StringBuilder.Extensions.Tests;

/// <summary>
/// The indentation state lives in a process-wide, non-thread-safe
/// <c>Dictionary&lt;StringBuilder, StringBuilderState&gt;</c> keyed by builder instance, with no
/// eviction. Every test here therefore uses a fresh StringBuilder, and the whole class runs
/// in one non-parallel collection so concurrent access cannot corrupt the dictionary.
///
/// Expectations are composed from Environment.NewLine, never written as literal "\r\n":
/// AppendIndented and Dedent both key off it, so a literal would pass on Windows and fail
/// on the Linux CI runner.
/// </summary>
[CollectionDefinition(nameof(StringBuilderStateCollection), DisableParallelization = true)]
public class StringBuilderStateCollection;

[Collection(nameof(StringBuilderStateCollection))]
public class StringBuilderExtensionsTests
{
    private static readonly string NL = Environment.NewLine;

    #region Indent / Unindent

    [Fact]
    public void AppendIndented_WithNoIndent_JustAppendsTheLine()
    {
        var builder = new SB();

        builder.AppendIndented("hello");

        builder.ToString().Should().Be("hello" + NL);
    }

    [Fact]
    public void AppendIndented_WithATabIndent_PrefixesTabs()
    {
        var builder = new SB();

        builder.Indent(EIndentationType.Tab, 2).AppendIndented("hello");

        builder.ToString().Should().Be("\t\thello" + NL);
    }

    [Fact]
    public void AppendIndented_WithASpaceIndent_PrefixesSpaces()
    {
        var builder = new SB();

        builder.Indent(EIndentationType.Space, 4).AppendIndented("hello");

        builder.ToString().Should().Be("    hello" + NL);
    }

    [Fact]
    public void Indent_Stacks()
    {
        var builder = new SB();

        builder
            .Indent(EIndentationType.Space, 2)
            .Indent(EIndentationType.Space, 2)
            .AppendIndented("hello");

        builder.ToString().Should().Be("    hello" + NL);
    }

    [Fact]
    public void Indent_MixesTabsAndSpaces()
    {
        var builder = new SB();

        builder
            .Indent(EIndentationType.Tab, 1)
            .Indent(EIndentationType.Space, 2)
            .AppendIndented("hello");

        // Indentations is a Stack, so the most recent push is rendered first.
        builder.ToString().Should().Be("  \thello" + NL);
    }

    [Fact]
    public void Unindent_RemovesTheInnermostIndent()
    {
        var builder = new SB();

        builder
            .Indent(EIndentationType.Space, 2)
            .Indent(EIndentationType.Space, 4)
            .Unindent()
            .AppendIndented("hello");

        builder.ToString().Should().Be("  hello" + NL);
    }

    [Fact]
    public void Unindent_BackToZero_RemovesAllIndentation()
    {
        var builder = new SB();

        builder.Indent(EIndentationType.Space, 2).Unindent().AppendIndented("hello");

        builder.ToString().Should().Be("hello" + NL);
    }

    [Fact]
    public void Unindent_OnAnUntrackedBuilder_Throws()
    {
        var builder = new SB();

        var act = () => builder.Unindent();

        act.Should().Throw<StringBuilderNotFoundException>();
    }

    [Fact]
    public void Unindent_MoreOftenThanIndented_Throws()
    {
        var builder = new SB();
        builder.Indent(EIndentationType.Space, 2).Unindent();

        var act = () => builder.Unindent();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Indent_ReturnsTheSameBuilderForChaining()
    {
        var builder = new SB();

        builder.Indent(EIndentationType.Tab, 1).Should().BeSameAs(builder);
    }

    [Fact]
    public void Indent_WithSizeZero_AddsNothing()
    {
        var builder = new SB();

        builder.Indent(EIndentationType.Space, 0).AppendIndented("hello");

        builder.ToString().Should().Be("hello" + NL);
    }

    [Fact]
    public void EachBuilderKeepsItsOwnIndentationState()
    {
        var first = new SB();
        var second = new SB();

        first.Indent(EIndentationType.Space, 4);
        second.AppendIndented("no indent");
        first.AppendIndented("indented");

        second.ToString().Should().Be("no indent" + NL);
        first.ToString().Should().Be("    indented" + NL);
    }

    #endregion

    #region AppendIndented multi-line

    [Fact]
    public void AppendIndented_IndentsEveryLine()
    {
        var builder = new SB();

        builder.Indent(EIndentationType.Space, 2).AppendIndented("one" + NL + "two");

        builder.ToString().Should().Be("  one" + NL + "  two" + NL);
    }

    [Fact]
    public void AppendIndented_IndentsThreeLines()
    {
        var builder = new SB();

        builder.Indent(EIndentationType.Space, 1).AppendIndented("a" + NL + "b" + NL + "c");

        builder.ToString().Should().Be(" a" + NL + " b" + NL + " c" + NL);
    }

    [Fact]
    public void AppendIndented_WithATrailingNewline_EmitsAnIndentedBlankLine()
    {
        var builder = new SB();

        builder.Indent(EIndentationType.Space, 2).AppendIndented("one" + NL);

        builder.ToString().Should().Be("  one" + NL + "  " + NL);
    }

    [Fact]
    public void AppendIndented_WithNull_IsANoOp()
    {
        var builder = new SB();

        builder.AppendIndented(null);

        builder.ToString().Should().Be(string.Empty);
    }

    /// <summary>
    /// Regression for D16 in docs/PRD-TestCoverage.md. After the final line, the method did
    /// <c>valueSpan.Slice(index + nl.Length)</c> with index == -1. On Windows
    /// Environment.NewLine is two characters, so that is Slice(1) on a span that has already
    /// been fully consumed — ArgumentOutOfRangeException for an empty input. On Linux
    /// NewLine is one character, so it was Slice(0) and the bug did not show. A
    /// platform-dependent crash on the simplest possible input.
    /// </summary>
    [Fact]
    public void AppendIndented_WithAnEmptyString_DoesNotThrow()
    {
        var builder = new SB();

        var act = () => builder.AppendIndented(string.Empty);

        act.Should().NotThrow();
        builder.ToString().Should().Be(NL);
    }

    [Fact]
    public void AppendIndented_WithAnEmptyStringAndAnIndent_EmitsTheIndent()
    {
        var builder = new SB();

        builder.Indent(EIndentationType.Space, 2).AppendIndented(string.Empty);

        builder.ToString().Should().Be("  " + NL);
    }

    [Fact]
    public void AppendIndented_ReturnsTheSameBuilderForChaining()
    {
        var builder = new SB();

        builder.AppendIndented("a").Should().BeSameAs(builder);
    }

    [Fact]
    public void AppendIndented_AppendsAfterExistingContent()
    {
        var builder = new SB("prefix: ");

        builder.AppendIndented("value");

        builder.ToString().Should().Be("prefix: value" + NL);
    }

    #endregion
}
