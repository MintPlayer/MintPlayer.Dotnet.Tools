using MintPlayer.SlnLaunch;
using MintPlayer.SlnLaunch.Models;
using MintPlayer.SlnLaunch.Services;

namespace MintPlayer.SlnLaunch.Tests;

public class SlnLaunchFileServiceTests
{
    private static readonly ISlnLaunchFileService _service = new SlnLaunchFileService();

    private const string SparkSample = """
        [
          {
            "Name": "HR + Fleet",
            "Projects": [
              { "Path": "Demo\\Fleet\\Fleet\\Fleet.csproj", "Action": "Start", "DebugTarget": "https" },
              { "Path": "Demo\\HR\\HR\\HR.csproj", "Action": "Start", "DebugTarget": "https" }
            ]
          }
        ]
        """;

    [Fact]
    public void Load_parses_the_spark_sample()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("MintPlayer.Spark.slnLaunch", SparkSample);

        var file = _service.Load(path);

        var profile = Assert.Single(file.Profiles);
        Assert.Equal("HR + Fleet", profile.Name);
        Assert.Equal(2, profile.Projects.Count);
        Assert.All(profile.Projects, p => Assert.Equal(LaunchAction.Start, p.Action));
        Assert.All(profile.Projects, p => Assert.Equal("https", p.DebugTarget));
        Assert.Equal(@"Demo\Fleet\Fleet\Fleet.csproj", profile.Projects[0].Path);
    }

    [Fact]
    public void Load_sets_directory_to_the_files_folder()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("App.slnLaunch", SparkSample);

        var file = _service.Load(path);

        Assert.Equal(temp.Path, file.Directory);
    }

    [Theory]
    [InlineData("Start", LaunchAction.Start)]
    [InlineData("StartWithoutDebugging", LaunchAction.StartWithoutDebugging)]
    [InlineData("None", LaunchAction.None)]
    [InlineData("start", LaunchAction.Start)] // enum matching is case-insensitive
    public void Load_maps_action_tokens(string token, LaunchAction expected)
    {
        using var temp = new TempDirectory();
        var json = $$"""
            [ { "Name": "P", "Projects": [ { "Path": "a.csproj", "Action": "{{token}}" } ] } ]
            """;
        var path = temp.WriteFile("App.slnLaunch", json);

        var file = _service.Load(path);

        Assert.Equal(expected, file.Profiles[0].Projects[0].Action);
    }

    [Fact]
    public void Load_treats_absent_action_as_none()
    {
        using var temp = new TempDirectory();
        var json = """[ { "Name": "P", "Projects": [ { "Path": "a.csproj" } ] } ]""";
        var path = temp.WriteFile("App.slnLaunch", json);

        var entry = _service.Load(path).Profiles[0].Projects[0];

        Assert.Equal(LaunchAction.None, entry.Action);
        Assert.False(entry.ShouldLaunch);
    }

    [Fact]
    public void Load_treats_absent_debugtarget_as_null()
    {
        using var temp = new TempDirectory();
        var json = """[ { "Name": "P", "Projects": [ { "Path": "a.csproj", "Action": "Start" } ] } ]""";
        var path = temp.WriteFile("App.slnLaunch", json);

        Assert.Null(_service.Load(path).Profiles[0].Projects[0].DebugTarget);
    }

    [Fact]
    public void Load_is_lenient_about_comments_and_trailing_commas()
    {
        using var temp = new TempDirectory();
        var json = """
            [
              // leading comment
              { "Name": "P", "Projects": [ { "Path": "a.csproj", "Action": "Start", }, ], }
            ]
            """;
        var path = temp.WriteFile("App.slnLaunch", json);

        Assert.Equal("P", _service.Load(path).Profiles[0].Name);
    }

    [Fact]
    public void Load_throws_for_missing_file()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "nope.slnLaunch");

        var ex = Assert.Throws<SlnLaunchException>(() => _service.Load(path));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_throws_for_malformed_json()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("App.slnLaunch", "{ this is not valid");

        Assert.Throws<SlnLaunchException>(() => _service.Load(path));
    }

    [Fact]
    public void Load_throws_for_empty_profile_array()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("App.slnLaunch", "[]");

        var ex = Assert.Throws<SlnLaunchException>(() => _service.Load(path));
        Assert.Contains("no launch profiles", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_throws_when_a_project_has_no_path()
    {
        using var temp = new TempDirectory();
        var json = """[ { "Name": "P", "Projects": [ { "Action": "Start" } ] } ]""";
        var path = temp.WriteFile("App.slnLaunch", json);

        Assert.Throws<SlnLaunchException>(() => _service.Load(path));
    }

    [Fact]
    public void Find_returns_empty_when_no_files()
    {
        using var temp = new TempDirectory();
        Assert.Empty(_service.Find(temp.Path));
    }

    [Fact]
    public void Find_returns_empty_for_missing_directory()
    {
        Assert.Empty(_service.Find(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Find_locates_a_single_slnLaunch()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("App.slnLaunch", SparkSample);

        var found = _service.Find(temp.Path);

        Assert.Equal(path, Assert.Single(found));
    }

    [Fact]
    public void Find_prefers_slnLaunch_over_user_and_slnx()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.slnLaunch", SparkSample);
        temp.WriteFile("App.slnLaunch.user", SparkSample);
        temp.WriteFile("App.slnxLaunch", SparkSample);

        var found = _service.Find(temp.Path);

        Assert.Equal("App.slnLaunch", Path.GetFileName(Assert.Single(found)));
    }

    [Fact]
    public void Find_falls_back_to_user_then_slnx()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("App.slnLaunch.user", SparkSample);
        temp.WriteFile("App.slnxLaunch", SparkSample);

        var found = _service.Find(temp.Path);

        Assert.Equal("App.slnLaunch.user", Path.GetFileName(Assert.Single(found)));
    }

    [Fact]
    public void Find_returns_all_matches_in_the_winning_tier()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("One.slnLaunch", SparkSample);
        temp.WriteFile("Two.slnLaunch", SparkSample);

        var found = _service.Find(temp.Path);

        Assert.Equal(2, found.Count);
    }
}
