using Microsoft.Extensions.DependencyInjection;
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

        Assert.Equal(0, code);
        Assert.False(orchestrator.WasCalled);
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

        Assert.Equal(0, code);
        Assert.False(orchestrator.WasCalled);
    }

    [Fact]
    public async Task Missing_file_returns_one()
    {
        var command = Build(new FakeConsole(), new FakeOrchestrator());
        command.FilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".slnLaunch");

        Assert.Equal(1, await command.Execute(CancellationToken.None));
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

        Assert.Equal(1, await command.Execute(CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_profile_returns_one()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("App.slnLaunch", TwoProjectsOneProfile);
        var command = Build(new FakeConsole(), new FakeOrchestrator());
        command.FilePath = path;
        command.Profile = "does-not-exist";

        Assert.Equal(1, await command.Execute(CancellationToken.None));
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

        Assert.Equal(42, code);
        Assert.True(orchestrator.WasCalled);
        Assert.Equal("All", orchestrator.Plan!.ProfileName);
        Assert.True(orchestrator.Options!.KillOnFail);
        Assert.True(orchestrator.Options!.NoPrefix);
        Assert.All(orchestrator.Plan!.Commands, c => Assert.Contains("Release", c.Arguments));
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
        Assert.Equal(["--tenant", "acme"], args.SkipWhile(a => a != "--").Skip(1));
        Assert.DoesNotContain("--ignored", args);
    }
}
