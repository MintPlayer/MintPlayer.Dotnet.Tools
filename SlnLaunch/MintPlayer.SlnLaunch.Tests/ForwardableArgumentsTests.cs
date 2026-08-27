using MintPlayer.Assertions;
using MintPlayer.SlnLaunch.Models;

namespace MintPlayer.SlnLaunch.Tests;

public class ForwardableArgumentsTests
{
    [Fact]
    public void Select_returns_option_with_value()
    {
        var pool = ForwardableArguments.Parse(["--tenant", "acme"]);
        pool.Select(["tenant"]).Should().Equal("--tenant", "acme");
    }

    [Fact]
    public void Select_matches_name_ignoring_dashes()
    {
        var pool = ForwardableArguments.Parse(["--tenant", "acme"]);
        pool.Select(["--tenant"]).Should().Equal("--tenant", "acme");
    }

    [Fact]
    public void Select_handles_flag_without_value()
    {
        var pool = ForwardableArguments.Parse(["--verbose", "--tenant", "acme"]);
        pool.Select(["verbose"]).Should().Equal("--verbose");
    }

    [Fact]
    public void Select_preserves_equals_form()
    {
        var pool = ForwardableArguments.Parse(["--tenant=acme"]);
        pool.Select(["tenant"]).Should().Equal("--tenant=acme");
    }

    [Fact]
    public void Select_keeps_requested_order_and_ignores_unknown()
    {
        var pool = ForwardableArguments.Parse(["--a", "1", "--b", "2"]);
        pool.Select(["b", "missing", "a"]).Should().Equal("--b", "2", "--a", "1");
    }

    [Fact]
    public void Select_includes_repeated_occurrences()
    {
        var pool = ForwardableArguments.Parse(["--inc", "x", "--inc", "y"]);
        pool.Select(["inc"]).Should().Equal("--inc", "x", "--inc", "y");
    }

    [Fact]
    public void Empty_pool_returns_nothing()
    {
        ForwardableArguments.Empty.Select(["anything"]).Should().BeEmpty();
    }

    [Fact]
    public void Parse_ignores_leading_positional_tokens()
    {
        var pool = ForwardableArguments.Parse(["stray", "--real", "v"]);
        pool.Select(["real"]).Should().Equal("--real", "v");
    }
}
