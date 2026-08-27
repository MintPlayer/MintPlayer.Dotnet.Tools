using Microsoft.Extensions.DependencyInjection;
using MintPlayer.Assertions;
using MintPlayer.Assertions.Execution;
using MintPlayer.SlnLaunch;
using MintPlayer.SlnLaunch.Commands;
using MintPlayer.SlnLaunch.Models;
using MintPlayer.SlnLaunch.Services;

namespace MintPlayer.SlnLaunch.Tests;

public class SlnLaunchCommandTests
{
    private static SlnLaunchCommand Build(FakeConsole console, FakeOrchestrator orchestrator, ForwardableArguments? pool = null)
    {
        var services = new ServiceCollection();
        services.AddSlnLaunchCommand().AddSlnLaunchServices();
        services.AddSingleton(pool ?? ForwardableArguments.Empty);
        services.AddSingleton<IConsoleService>(console);       // override the real ConsoleService
        services.AddSingleton<IProcessOrchestrator>(orchestrator);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<SlnLaunchCommand>();
    }

    private static string AddProject(TempDirectory temp, string relativePath)
    {
        temp.WriteFile(relativePath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        return relativePath;
    }

    private const string TwoProjectsOneProfile = """
        [ { "Name": "All", "Projects": [
            { "Path": "HR/HR.csproj", "Action": "Start", "DebugTarget": "https" },
            { "Path": "Fleet/Fleet.csproj", "Action": "Start" }
        ] } ]
        """;

    [Fact]
    public async Task List_prints_profiles_and_does_not_launch()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("App.slnLaunch", TwoProjectsOneProfile);
        var orchestrator = new FakeOrchestrator();
        var command = Build(new FakeConsole(), orchestrator);
        command.FilePath = path;
        command.List = true;

        var code = await command.Execute(CancellationToken.None);

        code.Should().Be(0);
        orchestrator.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task DryRun_builds_plan_but_does_not_launch()
    {
        using var temp = new TempDirectory();
        AddProject(temp, "HR/HR.csproj");
        AddProject(temp, "Fleet/Fleet.csproj");
        var path = temp.WriteFile("App.slnLaunch", TwoProjectsOneProfile);
        var orchestrator = new FakeOrchestrator();
        var command = Build(new FakeConsole(), orchestrator);
        command.FilePath = path;
        command.DryRun = true;

        var code = await command.Execute(CancellationToken.None);

        code.Should().Be(0);
        orchestrator.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_file_returns_one()
    {
        var command = Build(new FakeConsole(), new FakeOrchestrator());
        command.FilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".slnLaunch");

        (await command.Execute(CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task Multiple_profiles_without_selection_returns_one()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("App.slnLaunch", """
            [ { "Name": "A", "Projects": [] }, { "Name": "B", "Projects": [] } ]
            """);
        var command = Build(new FakeConsole(), new FakeOrchestrator());
        command.FilePath = path;

        (await command.Execute(CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task Unknown_profile_returns_one()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("App.slnLaunch", TwoProjectsOneProfile);
        var command = Build(new FakeConsole(), new FakeOrchestrator());
        command.FilePath = path;
        command.Profile = "does-not-exist";

        (await command.Execute(CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task Launches_and_passes_build_options_and_run_options()
    {
        using var temp = new TempDirectory();
        AddProject(temp, "HR/HR.csproj");
        AddProject(temp, "Fleet/Fleet.csproj");
        var path = temp.WriteFile("App.slnLaunch", TwoProjectsOneProfile);
        var orchestrator = new FakeOrchestrator { Result = 42 };
        var command = Build(new FakeConsole(), orchestrator);
        command.FilePath = path;
        command.Configuration = "Release";
        command.KillOnFail = true;
        command.NoPrefix = true;

        var code = await command.Execute(CancellationToken.None);

        using (new AssertionScope("the launch"))
        {
            code.Should().Be(42);
            orchestrator.WasCalled.Should().BeTrue();
            orchestrator.Plan!.ProfileName.Should().Be("All");
            orchestrator.Options!.KillOnFail.Should().BeTrue();
            orchestrator.Options!.NoPrefix.Should().BeTrue();
            orchestrator.Plan!.Commands.Should().AllSatisfy(c => c.Arguments.Should().Contain("Release"));
        }
    }

    [Fact]
    public async Task Default_launch_builds_first_then_runs_with_no_build()
    {
        using var temp = new TempDirectory();
        AddProject(temp, "HR/HR.csproj");
        AddProject(temp, "Fleet/Fleet.csproj");
        var path = temp.WriteFile("App.slnLaunch", TwoProjectsOneProfile);
        var orchestrator = new FakeOrchestrator();
        var command = Build(new FakeConsole(), orchestrator);
        command.FilePath = path;

        var code = await command.Execute(CancellationToken.None);

        using (new AssertionScope("the default launch"))
        {
            code.Should().Be(0);
            orchestrator.BuildWasCalled.Should().BeTrue();
            orchestrator.WasCalled.Should().BeTrue();
            orchestrator.BuildRanBeforeRun.Should().BeTrue();
            orchestrator.Plan!.Commands.Should().AllSatisfy(c => c.Arguments.Should().Contain("--no-build"));
        }
    }

    [Fact]
    public async Task Explicit_no_build_skips_build_phase_but_keeps_no_build_arg()
    {
        using var temp = new TempDirectory();
        AddProject(temp, "HR/HR.csproj");
        AddProject(temp, "Fleet/Fleet.csproj");
        var path = temp.WriteFile("App.slnLaunch", TwoProjectsOneProfile);
        var orchestrator = new FakeOrchestrator();
        var command = Build(new FakeConsole(), orchestrator);
        command.FilePath = path;
        command.NoBuild = true;

        var code = await command.Execute(CancellationToken.None);

        using (new AssertionScope("the explicit --no-build launch"))
        {
            code.Should().Be(0);
            orchestrator.BuildWasCalled.Should().BeFalse();
            orchestrator.WasCalled.Should().BeTrue();
            orchestrator.Plan!.Commands.Should().AllSatisfy(c => c.Arguments.Should().Contain("--no-build"));
        }
    }

    [Fact]
    public async Task Watch_skips_build_phase_and_does_not_pass_no_build()
    {
        using var temp = new TempDirectory();
        AddProject(temp, "HR/HR.csproj");
        AddProject(temp, "Fleet/Fleet.csproj");
        var path = temp.WriteFile("App.slnLaunch", TwoProjectsOneProfile);
        var orchestrator = new FakeOrchestrator();
        var command = Build(new FakeConsole(), orchestrator);
        command.FilePath = path;
        command.Watch = true;

        var code = await command.Execute(CancellationToken.None);

        using (new AssertionScope("the watch launch"))
        {
            code.Should().Be(0);
            orchestrator.BuildWasCalled.Should().BeFalse();
            orchestrator.WasCalled.Should().BeTrue();
            orchestrator.Plan!.Commands.Should().AllSatisfy(c => c.Arguments.Should().NotContain("--no-build"));
        }
    }

    [Fact]
    public async Task Build_failure_returns_one_and_does_not_launch()
    {
        using var temp = new TempDirectory();
        AddProject(temp, "HR/HR.csproj");
        AddProject(temp, "Fleet/Fleet.csproj");
        var path = temp.WriteFile("App.slnLaunch", TwoProjectsOneProfile);
        var orchestrator = new FakeOrchestrator { BuildResult = false };
        var command = Build(new FakeConsole(), orchestrator);
        command.FilePath = path;

        var code = await command.Execute(CancellationToken.None);

        using (new AssertionScope("the failed build"))
        {
            code.Should().Be(1);
            orchestrator.BuildWasCalled.Should().BeTrue();
            orchestrator.WasCalled.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Forwards_pooled_arguments_per_project()
    {
        using var temp = new TempDirectory();
        AddProject(temp, "HR/HR.csproj");
        var path = temp.WriteFile("App.slnLaunch", """
            [ { "Name": "All", "Projects": [
                { "Path": "HR/HR.csproj", "Action": "Start", "ForwardArguments": ["tenant"] }
            ] } ]
            """);
        var pool = ForwardableArguments.Parse(["--tenant", "acme", "--ignored", "x"]);
        var orchestrator = new FakeOrchestrator();
        var command = Build(new FakeConsole(), orchestrator, pool);
        command.FilePath = path;

        await command.Execute(CancellationToken.None);

        var args = orchestrator.Plan!.Commands.Single().Arguments;
        args.SkipWhile(a => a != "--").Skip(1).Should().Equal("--tenant", "acme");
        args.Should().NotContain("--ignored");
    }
}
