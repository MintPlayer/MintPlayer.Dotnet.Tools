using MintPlayer.Verz.Sdks.NodeJS;
using Xunit;

namespace MintPlayer.Verz.Sdks.NodeJS.Tests;

public class FrameworkDetectionTests
{
    private static Dictionary<string, string> Deps(params (string K, string V)[] entries) =>
        entries.ToDictionary(p => p.K, p => p.V);

    [Theory]
    [InlineData("17.0.0", 17)]
    [InlineData("^17.2.1", 17)]
    [InlineData("~16.8.0", 16)]
    [InlineData(">=15.0.0", 15)]
    [InlineData("= 18.2.3", 18)]
    [InlineData(">=17.0.0 <18", 17)]
    public void Range_prefix_parsing_extracts_major(string range, int expected)
    {
        Assert.True(FrameworkDetection.TryParseRangeMajor(range, out var major));
        Assert.Equal(expected, major);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("^abc")]
    public void Invalid_ranges_return_false(string range)
    {
        Assert.False(FrameworkDetection.TryParseRangeMajor(range, out _));
    }

    [Fact]
    public void Angular_in_dependencies_wins()
    {
        var deps = Deps(("@angular/core", "^17.2.1"), ("react", "18"));
        var peer = Deps();
        Assert.Equal(17, FrameworkDetection.DetectMajor(deps, peer));
    }

    [Fact]
    public void React_picked_when_angular_absent()
    {
        var deps = Deps(("react", "^18.2.0"));
        Assert.Equal(18, FrameworkDetection.DetectMajor(deps, Deps()));
    }

    [Fact]
    public void Vue_picked_when_others_absent()
    {
        var deps = Deps(("vue", "~3.4.0"));
        Assert.Equal(3, FrameworkDetection.DetectMajor(deps, Deps()));
    }

    [Fact]
    public void No_framework_returns_null()
    {
        var deps = Deps(("lodash", "^4.0.0"));
        Assert.Null(FrameworkDetection.DetectMajor(deps, Deps()));
    }

    [Fact]
    public void PeerDependencies_consulted_when_deps_lack_framework()
    {
        var deps = Deps(("lodash", "^4.0.0"));
        var peer = Deps(("@angular/core", "^16.0.0"));
        Assert.Equal(16, FrameworkDetection.DetectMajor(deps, peer));
    }

    [Fact]
    public void Dependencies_take_precedence_over_peer_for_same_framework()
    {
        var deps = Deps(("@angular/core", "^17.0.0"));
        var peer = Deps(("@angular/core", "^16.0.0"));
        Assert.Equal(17, FrameworkDetection.DetectMajor(deps, peer));
    }
}
