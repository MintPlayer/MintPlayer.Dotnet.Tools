using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.SourceGenerators.Tools.Tests;

public class StringExtensionsTests
{
    #region UcFirst

    /// <summary>
    /// Note this is NOT the same UcFirst as MintPlayer.StringExtensions.Casing.UcFirst —
    /// this one also lower-cases the remainder, because it is used to normalize
    /// namespace segments into a type-name fragment.
    /// </summary>
    [Theory]
    [InlineData("hello", "Hello")]
    [InlineData("HELLO", "Hello")]
    [InlineData("hELLO", "Hello")]
    [InlineData("h", "H")]
    [InlineData("H", "H")]
    public void UcFirst_CapitalizesTheFirstAndLowerCasesTheRest(string input, string expected)
        => input.UcFirst().Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UcFirst_OnBlankInput_ReturnsItUnchanged(string input)
        => input.UcFirst().Should().Be(input);

    #endregion

    #region RemoveBegin / RemoveEnd

    [Theory]
    [InlineData("global::Foo", "global::", "Foo")]
    [InlineData("Foo", "global::", "Foo")]
    [InlineData("abc", "", "abc")]
    [InlineData("abc", "abc", "")]
    public void RemoveBegin_StripsAMatchingPrefix(string input, string prefix, string expected)
        => input.RemoveBegin(prefix).Should().Be(expected);

    [Fact]
    public void RemoveBegin_WithNull_IsANoOp()
        => "abc".RemoveBegin(null!).Should().Be("abc");

    [Fact]
    public void RemoveBegin_RemovesOnlyOneOccurrence()
        => "aaabc".RemoveBegin("a").Should().Be("aabc");

    [Theory]
    [InlineData("Foo.cs", ".cs", "Foo")]
    [InlineData("Foo", ".cs", "Foo")]
    [InlineData("abc", "", "abc")]
    public void RemoveEnd_StripsAMatchingSuffix(string input, string suffix, string expected)
        => input.RemoveEnd(suffix).Should().Be(expected);

    [Fact]
    public void RemoveEnd_WithNull_IsANoOp()
        => "abc".RemoveEnd(null!).Should().Be("abc");

    #endregion

    #region EnsureStartsWith / global

    [Fact]
    public void EnsureStartsWith_PrependsWhenMissing()
        => "Foo".EnsureStartsWith("global::").Should().Be("global::Foo");

    [Fact]
    public void EnsureStartsWith_IsIdempotent()
        => "global::Foo".EnsureStartsWith("global::").Should().Be("global::Foo");

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void EnsureStartsWith_OnBlankInput_ReturnsItUnchanged(string input)
        => input.EnsureStartsWith("global::").Should().Be(input);

    [Fact]
    public void WithGlobal_AddsThePrefix()
        => "System.String".WithGlobal().Should().Be("global::System.String");

    [Fact]
    public void WithGlobal_IsIdempotent()
        => "global::System.String".WithGlobal().Should().Be("global::System.String");

    [Fact]
    public void WithoutGlobal_StripsThePrefix()
        => "global::System.String".WithoutGlobal().Should().Be("System.String");

    [Fact]
    public void WithoutGlobal_OnAnUnprefixedName_IsANoOp()
        => "System.String".WithoutGlobal().Should().Be("System.String");

    [Fact]
    public void WithGlobal_AndWithoutGlobal_RoundTrip()
        => "System.Collections.Generic.List".WithGlobal().WithoutGlobal()
            .Should().Be("System.Collections.Generic.List");

    #endregion

    #region StringifyTypeName

    [Theory]
    [InlineData("System.String", "SystemString")]
    [InlineData("global::System.String", "SystemString")]
    [InlineData("MY.name.SPACE", "MyNameSpace")]
    [InlineData("Foo", "Foo")]
    public void StringifyTypeName_PascalCasesEachSegmentAndJoins(string input, string expected)
        => input.StringifyTypeName().Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StringifyTypeName_OnBlankInput_ReturnsItUnchanged(string input)
        => input.StringifyTypeName().Should().Be(input);

    #endregion

    #region EscapeForStringLiteral / ToStringLiteral

    [Fact]
    public void EscapeForStringLiteral_EscapesBackslashesBeforeQuotes()
    {
        // Order matters: escaping quotes first would then double the new backslashes.
        var input = "a" + '\\' + "b" + '"' + "c";

        input.EscapeForStringLiteral().Should().Be("a" + @"\\" + "b" + @"\""" + "c");
    }

    [Fact]
    public void EscapeForStringLiteral_EscapesNewlines()
        => "a\r\nb".EscapeForStringLiteral().Should().Be(@"a\r\nb");

    [Fact]
    public void EscapeForStringLiteral_LeavesPlainTextAlone()
        => "plain text".EscapeForStringLiteral().Should().Be("plain text");

    [Fact]
    public void EscapeForStringLiteral_OnEmpty_IsEmpty()
        => string.Empty.EscapeForStringLiteral().Should().Be(string.Empty);

    [Fact]
    public void ToStringLiteral_QuotesAndEscapes()
        => "a\"b".ToStringLiteral().Should().Be(@"""a\""b""");

    [Fact]
    public void ToStringLiteral_OnNull_IsTheNullKeyword()
        => ((string?)null).ToStringLiteral().Should().Be("null");

    [Fact]
    public void ToStringLiteral_OnEmpty_IsAnEmptyLiteral()
        => string.Empty.ToStringLiteral().Should().Be("\"\"");

    [Fact]
    public void ToStringLiteral_ProducesSomethingRoslynCanParse()
    {
        var literal = ("line1" + "\r\n" + "with " + '"' + " and " + '\\').ToStringLiteral();

        var expression = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(literal);

        expression.GetDiagnostics().Should().BeEmpty();
    }

    #endregion
}
