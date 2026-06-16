using MintPlayer.SlnLaunch;
using MintPlayer.SlnLaunch.Models;
using MintPlayer.SlnLaunch.Services;

namespace MintPlayer.SlnLaunch.Tests;

public class LaunchPlanBuilderTests
{
    private static readonly ILaunchPlanBuilder _builder = new LaunchPlanBuilder();

    private static LaunchProfile Profile(params LaunchProjectEntry[] projects)
        => new() { Name = "P", Projects = [.. projects] };

    private static LaunchProjectEntry Entry(string path, LaunchAction action = LaunchAction.Start, string? debugTarget = null)
        => new() { Path = path, Action = action, DebugTarget = debugTarget };

    /// <summary>Creates an empty project file under the temp dir and returns its sln-relative path.</summary>
    private static string AddProject(TempDirectory temp, string relativePath, string? launchSettings = null)
    {
        temp.WriteFile(relativePath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        if (launchSettings is not null)
        {
            var dir = Path.GetDirectoryName(relativePath)!;
            temp.WriteFile(Path.Combine(dir, "Properties", "launchSettings.json"), launchSettings);
        }
        return relativePath;
    }

    private static string ProjectProfile(string name) => $$"""
        { "profiles": { "{{name}}": { "commandName": "Project", "applicationUrl": "https://localhost:5001" } } }
        """;

    [Fact]
    public void Build_passes_debugtarget_as_launch_profile()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj", ProjectProfile("https"));

        var plan = _builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, watch: false);

        var cmd = Assert.Single(plan.Commands);
        Assert.Equal("App", cmd.Label);
        Assert.Equal("https", cmd.LaunchProfile);
        Assert.Equal(["run", "--project", cmd.ProjectPath, "--launch-profile", "https"], cmd.Arguments);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Build_resolves_backslash_relative_path_to_absolute_existing_file()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"src\Api\Api.csproj");

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel)), temp.Path, watch: false).Commands);

        Assert.True(Path.IsPathRooted(cmd.ProjectPath));
        Assert.True(File.Exists(cmd.ProjectPath));
        Assert.Equal("Api", cmd.Label);
    }

    [Fact]
    public void Build_omits_launch_profile_when_debugtarget_absent()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj");

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel)), temp.Path, watch: false).Commands);

        Assert.Null(cmd.LaunchProfile);
        Assert.DoesNotContain("--launch-profile", cmd.Arguments);
    }

    [Fact]
    public void Build_warns_and_drops_profile_for_non_project_target()
    {
        using var temp = new TempDirectory();
        var settings = """{ "profiles": { "IIS Express": { "commandName": "IISExpress" } } }""";
        var rel = AddProject(temp, @"App\App.csproj", settings);

        var plan = _builder.Build(Profile(Entry(rel, debugTarget: "IIS Express")), temp.Path, watch: false);

        var cmd = Assert.Single(plan.Commands);
        Assert.Null(cmd.LaunchProfile);
        Assert.DoesNotContain("--launch-profile", cmd.Arguments);
        Assert.Contains(plan.Warnings, w => w.Contains("IIS Express") && w.Contains("without a launch profile"));
    }

    [Fact]
    public void Build_passes_through_when_launchsettings_missing()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj"); // no launchSettings.json

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, watch: false).Commands);

        Assert.Equal("https", cmd.LaunchProfile);
    }

    [Fact]
    public void Build_passes_through_when_profile_not_listed_in_launchsettings()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj", ProjectProfile("http"));

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, watch: false).Commands);

        Assert.Equal("https", cmd.LaunchProfile);
    }

    [Fact]
    public void Build_uses_watch_verb()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj", ProjectProfile("https"));

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, watch: true).Commands);

        Assert.Equal("watch", cmd.Arguments[0]);
        Assert.Equal(["watch", "--project", cmd.ProjectPath, "--launch-profile", "https"], cmd.Arguments);
    }

    [Fact]
    public void Build_skips_none_and_absent_actions()
    {
        using var temp = new TempDirectory();
        var started = AddProject(temp, @"A\A.csproj");
        var skipped = AddProject(temp, @"B\B.csproj");

        var plan = _builder.Build(
            Profile(Entry(started, LaunchAction.Start), Entry(skipped, LaunchAction.None)),
            temp.Path, watch: false);

        var cmd = Assert.Single(plan.Commands);
        Assert.Equal("A", cmd.Label);
    }

    [Fact]
    public void Build_skips_dcproj_with_warning()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"Compose\Compose.dcproj");

        var plan = _builder.Build(Profile(Entry(rel)), temp.Path, watch: false);

        Assert.Empty(plan.Commands);
        Assert.Contains(plan.Warnings, w => w.Contains("Compose") && w.Contains(".dcproj"));
    }

    [Fact]
    public void Build_throws_when_project_file_missing()
    {
        using var temp = new TempDirectory();

        var ex = Assert.Throws<SlnLaunchException>(
            () => _builder.Build(Profile(Entry(@"Ghost\Ghost.csproj")), temp.Path, watch: false));
        Assert.Contains("Ghost", ex.Message);
    }

    [Fact]
    public void Build_keeps_project_profile_with_spaces()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj", ProjectProfile("With Stubs"));

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel, debugTarget: "With Stubs")), temp.Path, watch: false).Commands);

        Assert.Equal("With Stubs", cmd.LaunchProfile);
        Assert.Contains("\"With Stubs\"", cmd.ToDisplayString());
    }
}
