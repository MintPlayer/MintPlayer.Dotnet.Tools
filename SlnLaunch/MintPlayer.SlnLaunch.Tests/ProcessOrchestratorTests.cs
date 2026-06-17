using System.Diagnostics;
using MintPlayer.SlnLaunch.Models;
using MintPlayer.SlnLaunch.Services;

namespace MintPlayer.SlnLaunch.Tests;

public class ProcessOrchestratorTests
{
    private static readonly LaunchRunOptions FastTeardown = new() { GraceTimeout = TimeSpan.FromMilliseconds(200) };

    private static (string file, string[] args) Sleeper() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", "ping -n 300 127.0.0.1 > NUL"])
            : ("/bin/sh", ["-c", "sleep 300"]);

    private static (string file, string[] args) ExitWith(int code) =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", $"exit {code}"])
            : ("/bin/sh", ["-c", $"exit {code}"]);

    private static LaunchCommand Cmd(string label, (string file, string[] args) spec) =>
        new(label, spec.file, "(test)", Path.GetTempPath(), spec.args, launchProfile: null);

    private static LaunchPlan Plan(params LaunchCommand[] commands) => new("test", commands, []);

    [Fact]
    public async Task Returns_zero_when_all_exit_successfully()
    {
        var code = await new ProcessOrchestrator(new FakeConsole())
            .RunAsync(Plan(Cmd("a", ExitWith(0)), Cmd("b", ExitWith(0))), FastTeardown, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Returns_first_nonzero_exit_code()
    {
        var code = await new ProcessOrchestrator(new FakeConsole())
            .RunAsync(Plan(Cmd("a", ExitWith(0)), Cmd("b", ExitWith(3))), FastTeardown, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(3, code);
    }

    [Fact]
    public async Task Returns_one_when_a_process_fails_to_start()
    {
        var plan = Plan(Cmd("ghost", ("this-executable-does-not-exist-xyz", [])));

        var code = await new ProcessOrchestrator(new FakeConsole())
            .RunAsync(plan, FastTeardown, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, code);
    }

    [Fact]
    public async Task Returns_zero_for_empty_plan()
    {
        var code = await new ProcessOrchestrator(new FakeConsole())
            .RunAsync(Plan(), FastTeardown, CancellationToken.None);

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Cancellation_tears_down_and_returns_zero()
    {
        var orchestrator = new ProcessOrchestrator(new FakeConsole());
        using var cts = new CancellationTokenSource();

        var run = orchestrator.RunAsync(Plan(Cmd("sleeper", Sleeper())), FastTeardown, cts.Token);
        await Task.Delay(500); // let it start
        cts.Cancel();

        var code = await run.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task KillOnFail_stops_siblings_when_one_fails()
    {
        var options = new LaunchRunOptions { KillOnFail = true, GraceTimeout = TimeSpan.FromMilliseconds(200) };
        var plan = Plan(Cmd("sleeper", Sleeper()), Cmd("failer", ExitWith(7)));

        // Completes quickly only if the sleeper is actually torn down (it would otherwise sleep 300s).
        var code = await new ProcessOrchestrator(new FakeConsole())
            .RunAsync(plan, options, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(7, code);
    }

    [Fact]
    public async Task Cancellation_kills_the_whole_process_tree()
    {
        // The grandchild-PID assertion is POSIX-only; Windows tree-kill is verified manually (see plan).
        if (OperatingSystem.IsWindows())
            return;

        var console = new FakeConsole();
        var orchestrator = new ProcessOrchestrator(console);
        // sh starts a background `sleep` grandchild and prints its PID, then waits.
        var plan = Plan(new LaunchCommand("tree", "/bin/sh", "(test)", Path.GetTempPath(),
            ["-c", "sleep 300 & echo GC:$!; wait"], launchProfile: null));

        using var cts = new CancellationTokenSource();
        var run = orchestrator.RunAsync(plan, FastTeardown, cts.Token);

        var grandchildPid = await WaitForGrandchildPid(console, TimeSpan.FromSeconds(10));
        Assert.True(ProcessIsAlive(grandchildPid), "grandchild should be alive before cancellation");

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(
            await WaitUntil(() => !ProcessIsAlive(grandchildPid), TimeSpan.FromSeconds(10)),
            "the grandchild process should have been killed with the tree");
    }

    private static async Task<int> WaitForGrandchildPid(FakeConsole console, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var line = console.ChildLines.FirstOrDefault(l => l.StartsWith("GC:", StringComparison.Ordinal));
            if (line is not null && int.TryParse(line.AsSpan(3), out var pid))
                return pid;
            await Task.Delay(50);
        }
        throw new TimeoutException("Did not observe the grandchild PID in time.");
    }

    private static bool ProcessIsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(100);
        }
        return condition();
    }
}
