using MintPlayer.SlnLaunch;
using MintPlayer.SlnLaunch.Models;
using MintPlayer.SlnLaunch.Services;

namespace MintPlayer.SlnLaunch.Tests;

public class LaunchPlanBuilderTests
{
    private static readonly ILaunchPlanBuilder _builder = new LaunchPlanBuilder();

    private static LaunchProfile Profile(params LaunchProjectEntry[] projects)
        => new() { Name = "P", Projects = [.. projects] };

    private static LaunchProjectEntry Entry(string path, LaunchAction action = LaunchAction.Start, string? debugTarget = null, params string[] forwardArguments)
        => new() { Path = path, Action = action, DebugTarget = debugTarget, ForwardArguments = [.. forwardArguments] };

    private static LaunchPlanOptions Opts(bool watch = false) => new() { Watch = watch };

    /// <summary>
    /// Creates an empty project file under the temp dir and returns its sln-relative path.
    /// The returned path keeps its original (possibly backslash) form so the builder's separator
    /// normalization is exercised; the file itself is written at an OS-normalized path so the test
    /// works on Linux/macOS too (where '\' is a literal filename character, not a separator).
    /// </summary>
    private static string AddProject(TempDirectory temp, string relativePath, string? launchSettings = null)
    {
        var osPath = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        temp.WriteFile(osPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        if (launchSettings is not null)
        {
            var dir = Path.GetDirectoryName(osPath)!;
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

        var plan = _builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, Opts());

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

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel)), temp.Path, Opts()).Commands);

        Assert.True(Path.IsPathRooted(cmd.ProjectPath));
        Assert.True(File.Exists(cmd.ProjectPath));
        Assert.Equal("Api", cmd.Label);
    }

    [Fact]
    public void Build_omits_launch_profile_when_debugtarget_absent()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj");

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel)), temp.Path, Opts()).Commands);

        Assert.Null(cmd.LaunchProfile);
        Assert.DoesNotContain("--launch-profile", cmd.Arguments);
    }

    [Fact]
    public void Build_warns_and_drops_profile_for_non_project_target()
    {
        using var temp = new TempDirectory();
        var settings = """{ "profiles": { "IIS Express": { "commandName": "IISExpress" } } }""";
        var rel = AddProject(temp, @"App\App.csproj", settings);

        var plan = _builder.Build(Profile(Entry(rel, debugTarget: "IIS Express")), temp.Path, Opts());

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

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, Opts()).Commands);

        Assert.Equal("https", cmd.LaunchProfile);
    }

    [Fact]
    public void Build_passes_through_when_profile_not_listed_in_launchsettings()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj", ProjectProfile("http"));

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, Opts()).Commands);

        Assert.Equal("https", cmd.LaunchProfile);
    }

    [Fact]
    public void Build_uses_watch_verb()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj", ProjectProfile("https"));

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, Opts(watch: true)).Commands);

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
            temp.Path, Opts());

        var cmd = Assert.Single(plan.Commands);
        Assert.Equal("A", cmd.Label);
    }

    [Fact]
    public void Build_skips_dcproj_with_warning()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"Compose\Compose.dcproj");

        var plan = _builder.Build(Profile(Entry(rel)), temp.Path, Opts());

        Assert.Empty(plan.Commands);
        Assert.Contains(plan.Warnings, w => w.Contains("Compose") && w.Contains(".dcproj"));
    }

    [Fact]
    public void Build_throws_when_project_file_missing()
    {
        using var temp = new TempDirectory();

        var ex = Assert.Throws<SlnLaunchException>(
            () => _builder.Build(Profile(Entry(@"Ghost\Ghost.csproj")), temp.Path, Opts()));
        Assert.Contains("Ghost", ex.Message);
    }

    [Fact]
    public void Build_keeps_project_profile_with_spaces()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj", ProjectProfile("With Stubs"));

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel, debugTarget: "With Stubs")), temp.Path, Opts()).Commands);

        Assert.Equal("With Stubs", cmd.LaunchProfile);
        Assert.Contains("\"With Stubs\"", cmd.ToDisplayString());
    }

    [Fact]
    public void Build_forwards_shared_build_options()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj");
        var options = new LaunchPlanOptions { Configuration = "Release", Framework = "net10.0", NoBuild = true, Verbosity = "minimal" };

        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel)), temp.Path, options).Commands);

        Assert.Equal(
            ["run", "--project", cmd.ProjectPath, "--configuration", "Release", "--framework", "net10.0", "--no-build", "--verbosity", "minimal"],
            cmd.Arguments);
    }

    [Fact]
    public void Build_forwards_only_opted_in_arguments_per_project()
    {
        using var temp = new TempDirectory();
        var hr = AddProject(temp, @"HR\HR.csproj");
        var fleet = AddProject(temp, @"Fleet\Fleet.csproj");
        var pool = ForwardableArguments.Parse(["--tenant", "acme", "--region", "eu", "--port", "5005"]);
        var options = new LaunchPlanOptions { ForwardableArguments = pool };

        var plan = _builder.Build(
            Profile(
                Entry(hr, forwardArguments: ["tenant", "region"]),
                Entry(fleet, forwardArguments: ["port"])),
            temp.Path, options);

        var hrCmd = plan.Commands.Single(c => c.Label == "HR");
        var fleetCmd = plan.Commands.Single(c => c.Label == "Fleet");

        Assert.Equal(["run", "--project", hrCmd.ProjectPath, "--", "--tenant", "acme", "--region", "eu"], hrCmd.Arguments);
        Assert.Equal(["run", "--project", fleetCmd.ProjectPath, "--", "--port", "5005"], fleetCmd.Arguments);
    }

    [Fact]
    public void Build_emits_no_separator_when_nothing_forwarded()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj");
        var pool = ForwardableArguments.Parse(["--tenant", "acme"]);

        // Project opts into nothing.
        var cmd = Assert.Single(_builder.Build(Profile(Entry(rel)), temp.Path, new LaunchPlanOptions { ForwardableArguments = pool }).Commands);

        Assert.DoesNotContain("--", cmd.Arguments);
    }
}
