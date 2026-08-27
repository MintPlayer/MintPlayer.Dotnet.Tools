using MintPlayer.Assertions;
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

        var cmd = plan.Commands.Should().ContainSingle().Which;
        using (new AssertionScope("the launch command"))
        {
            cmd.Label.Should().Be("App");
            cmd.LaunchProfile.Should().Be("https");
            cmd.Arguments.Should().Equal("run", "--project", cmd.ProjectPath, "--launch-profile", "https");
            plan.Warnings.Should().BeEmpty();
        }
    }

    [Fact]
    public void Build_resolves_backslash_relative_path_to_absolute_existing_file()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"src\Api\Api.csproj");

        var cmd = _builder.Build(Profile(Entry(rel)), temp.Path, Opts()).Commands.Should().ContainSingle().Which;

        using (new AssertionScope("the resolved project path"))
        {
            Path.IsPathRooted(cmd.ProjectPath).Should().BeTrue();
            File.Exists(cmd.ProjectPath).Should().BeTrue();
            cmd.Label.Should().Be("Api");
        }
    }

    [Fact]
    public void Build_omits_launch_profile_when_debugtarget_absent()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj");

        var cmd = _builder.Build(Profile(Entry(rel)), temp.Path, Opts()).Commands.Should().ContainSingle().Which;

        cmd.LaunchProfile.Should().BeNull();
        cmd.Arguments.Should().NotContain("--launch-profile");
    }

    [Fact]
    public void Build_warns_and_drops_profile_for_non_project_target()
    {
        using var temp = new TempDirectory();
        var settings = """{ "profiles": { "IIS Express": { "commandName": "IISExpress" } } }""";
        var rel = AddProject(temp, @"App\App.csproj", settings);

        var plan = _builder.Build(Profile(Entry(rel, debugTarget: "IIS Express")), temp.Path, Opts());

        var cmd = plan.Commands.Should().ContainSingle().Which;
        using (new AssertionScope("the dropped launch profile"))
        {
            cmd.LaunchProfile.Should().BeNull();
            cmd.Arguments.Should().NotContain("--launch-profile");
            plan.Warnings.Should().Contain(w => w.Contains("IIS Express") && w.Contains("without a launch profile"));
        }
    }

    [Fact]
    public void Build_passes_through_when_launchsettings_missing()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj"); // no launchSettings.json

        var cmd = _builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, Opts()).Commands.Should().ContainSingle().Which;

        cmd.LaunchProfile.Should().Be("https");
    }

    [Fact]
    public void Build_passes_through_when_profile_not_listed_in_launchsettings()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj", ProjectProfile("http"));

        var cmd = _builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, Opts()).Commands.Should().ContainSingle().Which;

        cmd.LaunchProfile.Should().Be("https");
    }

    [Fact]
    public void Build_uses_watch_verb()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj", ProjectProfile("https"));

        var cmd = _builder.Build(Profile(Entry(rel, debugTarget: "https")), temp.Path, Opts(watch: true)).Commands.Should().ContainSingle().Which;

        cmd.Arguments[0].Should().Be("watch");
        cmd.Arguments.Should().Equal("watch", "--project", cmd.ProjectPath, "--launch-profile", "https");
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

        plan.Commands.Should().ContainSingle().Which.Label.Should().Be("A");
    }

    [Fact]
    public void Build_skips_dcproj_with_warning()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"Compose\Compose.dcproj");

        var plan = _builder.Build(Profile(Entry(rel)), temp.Path, Opts());

        using (new AssertionScope("the dcproj plan"))
        {
            plan.Commands.Should().BeEmpty();
            plan.Warnings.Should().Contain(w => w.Contains("Compose") && w.Contains(".dcproj"));
        }
    }

    [Fact]
    public void Build_throws_when_project_file_missing()
    {
        using var temp = new TempDirectory();

        var act = () => _builder.Build(Profile(Entry(@"Ghost\Ghost.csproj")), temp.Path, Opts());

        act.Should().Throw<SlnLaunchException>().Which.Message.Should().Contain("Ghost");
    }

    [Fact]
    public void Build_keeps_project_profile_with_spaces()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj", ProjectProfile("With Stubs"));

        var cmd = _builder.Build(Profile(Entry(rel, debugTarget: "With Stubs")), temp.Path, Opts()).Commands.Should().ContainSingle().Which;

        cmd.LaunchProfile.Should().Be("With Stubs");
        cmd.ToDisplayString().Should().Contain("\"With Stubs\"");
    }

    [Fact]
    public void Build_forwards_shared_build_options()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj");
        var options = new LaunchPlanOptions { Configuration = "Release", Framework = "net10.0", NoBuild = true, Verbosity = "minimal" };

        var cmd = _builder.Build(Profile(Entry(rel)), temp.Path, options).Commands.Should().ContainSingle().Which;

        cmd.Arguments.Should().Equal(
            "run", "--project", cmd.ProjectPath, "--configuration", "Release", "--framework", "net10.0", "--no-build", "--verbosity", "minimal");
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

        using (new AssertionScope("the per-project forwarded arguments"))
        {
            hrCmd.Arguments.Should().Equal("run", "--project", hrCmd.ProjectPath, "--", "--tenant", "acme", "--region", "eu");
            fleetCmd.Arguments.Should().Equal("run", "--project", fleetCmd.ProjectPath, "--", "--port", "5005");
        }
    }

    [Fact]
    public void Build_emits_no_separator_when_nothing_forwarded()
    {
        using var temp = new TempDirectory();
        var rel = AddProject(temp, @"App\App.csproj");
        var pool = ForwardableArguments.Parse(["--tenant", "acme"]);

        // Project opts into nothing.
        var cmd = _builder.Build(Profile(Entry(rel)), temp.Path, new LaunchPlanOptions { ForwardableArguments = pool }).Commands.Should().ContainSingle().Which;

        cmd.Arguments.Should().NotContain("--");
    }
}
