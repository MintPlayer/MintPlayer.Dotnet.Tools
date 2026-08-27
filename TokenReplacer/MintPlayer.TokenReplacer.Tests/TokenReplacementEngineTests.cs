using MintPlayer.Assertions;
using MintPlayer.Assertions.Execution;
using MintPlayer.TokenReplacer.Targets;

namespace MintPlayer.TokenReplacer.Tests;

public class TokenReplacementEngineTests
{
    private static Dictionary<string, string> Tokens(params (string Name, string Value)[] tokens)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in tokens)
            dict[name] = value;
        return dict;
    }

    [Fact]
    public void Replaces_Single_Token()
    {
        var result = TokenReplacementEngine.Replace("v=$version$", Tokens(("version", "1.2.3")));

        using (new AssertionScope("the replacement result"))
        {
            result.Content.Should().Be("v=1.2.3");
            result.ReplacedCount.Should().Be(1);
            result.UnmatchedTokens.Should().BeEmpty();
        }
    }

    [Fact]
    public void Replaces_Multiple_Tokens_And_Occurrences()
    {
        var result = TokenReplacementEngine.Replace(
            "$greeting$ $name$! Again: $greeting$.",
            Tokens(("greeting", "Hello"), ("name", "World")));

        result.Content.Should().Be("Hello World! Again: Hello.");
        result.ReplacedCount.Should().Be(3);
    }

    [Fact]
    public void Token_Matching_Is_Case_Insensitive_With_Case_Insensitive_Dictionary()
    {
        var result = TokenReplacementEngine.Replace("$VERSION$", Tokens(("version", "2.0.0")));

        result.Content.Should().Be("2.0.0");
    }

    [Fact]
    public void Unknown_Tokens_Are_Left_And_Reported_Once()
    {
        var result = TokenReplacementEngine.Replace("$a$ $unknown$ $unknown$", Tokens(("a", "x")));

        result.Content.Should().Be("x $unknown$ $unknown$");
        result.UnmatchedTokens.Should().Equal("unknown");
    }

    [Fact]
    public void MSBuild_Property_Syntax_Is_Not_A_Token()
    {
        var content = "path=$(OutputPath) v=$version$";
        var result = TokenReplacementEngine.Replace(content, Tokens(("version", "1.0")));

        result.Content.Should().Be("path=$(OutputPath) v=1.0");
        result.UnmatchedTokens.Should().BeEmpty();
    }

    [Fact]
    public void Custom_Delimiters()
    {
        var result = TokenReplacementEngine.Replace("v={{version}}", Tokens(("version", "3.1.4")), "{{", "}}");

        result.Content.Should().Be("v=3.1.4");
    }

    [Fact]
    public void Replacement_Value_Containing_Delimiter_Is_Not_Reprocessed()
    {
        var result = TokenReplacementEngine.Replace("$a$$b$", Tokens(("a", "$b$"), ("b", "B")));

        // Single pass left-to-right: $a$ -> "$b$", then the original $b$ -> "B"
        result.Content.Should().Be("$b$B");
        result.ReplacedCount.Should().Be(2);
    }

    [Fact]
    public void Token_Names_Allow_Dots_Dashes_Underscores()
    {
        var result = TokenReplacementEngine.Replace(
            "$my.token-name_1$",
            Tokens(("my.token-name_1", "ok")));

        result.Content.Should().Be("ok");
    }

    [Fact]
    public void Empty_Content_Is_Returned_As_Is()
    {
        var result = TokenReplacementEngine.Replace("", Tokens(("a", "x")));

        result.Content.Should().BeEmpty();
        result.ReplacedCount.Should().Be(0);
    }

    [Fact]
    public void Content_Without_Tokens_Is_Unchanged()
    {
        const string content = "Plain text. Costs $5 and $10 together.";
        var result = TokenReplacementEngine.Replace(content, Tokens(("version", "1.0")));

        // "$5 and $10 together" — "$5 and $10" is not a valid token pair ($5 and $10... "5 and ..." contains spaces)
        result.Content.Should().Be(content);
        result.ReplacedCount.Should().Be(0);
    }

    [Fact]
    public void Replacement_Is_Idempotent_When_Values_Contain_No_Tokens()
    {
        var tokens = Tokens(("version", "1.2.3"));
        var once = TokenReplacementEngine.Replace("v=$version$", tokens).Content;
        var twice = TokenReplacementEngine.Replace(once, tokens).Content;

        twice.Should().Be(once);
    }

    [Fact]
    public void Empty_Delimiters_Throw()
    {
        var emptyStart = () => TokenReplacementEngine.Replace("x", Tokens(), "", "$");
        var emptyEnd = () => TokenReplacementEngine.Replace("x", Tokens(), "$", "");

        emptyStart.Should().Throw<ArgumentException>();
        emptyEnd.Should().Throw<ArgumentException>();
    }
}
