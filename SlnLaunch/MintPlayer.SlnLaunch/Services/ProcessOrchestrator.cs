using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SlnLaunch.Models;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.SlnLaunch.Services;

[Register(typeof(IProcessOrchestrator), ServiceLifetime.Singleton, "SlnLaunchServices")]
internal sealed class ProcessOrchestrator : IProcessOrchestrator
{
    // Distinct, readable colors cycled across projects for output prefixes.
    private static readonly ConsoleColor[] _palette =
    [
        ConsoleColor.Cyan, ConsoleColor.Green, ConsoleColor.Magenta,
        ConsoleColor.Yellow, ConsoleColor.Blue, ConsoleColor.DarkCyan,
    ];

    private readonly IConsoleService _console;

    public ProcessOrchestrator(IConsoleService console) => _console = console;

    public async Task<int> RunAsync(LaunchPlan plan, LaunchRunOptions options, CancellationToken cancellationToken)
    {
        if (plan.Commands.Count == 0)
        {
            _console.WriteWarning("Nothing to launch.");
            return 0;
        }

        var running = new List<RunningProcess>();
        var startFailed = false;

        // Cancelled by the caller (Ctrl+C/SIGTERM) OR internally on a kill-on-fail / start failure.
        using var teardownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        for (var i = 0; i < plan.Commands.Count; i++)
        {
            var command = plan.Commands[i];
            var color = _palette[i % _palette.Length];
            try
            {
                var process = StartProcess(command, color, options.NoPrefix);
                running.Add(new RunningProcess(command, process, color));
                _console.WriteInfo($"▶ [{command.Label}] {command.ToDisplayString()}");
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                _console.WriteError($"Failed to start '{command.Label}': {ex.Message}");
                startFailed = true;
            }
        }

        if (running.Count == 0)
            return startFailed ? 1 : 0;

        if (startFailed)
            teardownCts.Cancel(); // something didn't start — bring down whatever did.

        var monitors = running.Select(rp => MonitorAsync(rp, options, teardownCts)).ToArray();

        // Proceed to teardown as soon as either everything exits on its own or a teardown is requested.
        await WhenAllOrCancelled(Task.WhenAll(monitors), teardownCts.Token);

        await TeardownAsync(running, options.GraceTimeout);

        await Task.WhenAll(monitors); // all processes are dead now; exit codes recorded.

        if (startFailed)
            return 1;
        if (cancellationToken.IsCancellationRequested)
        {
            _console.WriteWarning("Cancelled.");
            return 0;
        }

        var firstFailure = running
            .Select(r => r.NaturalExitCode)
            .FirstOrDefault(code => code is not null and not 0);
        return firstFailure ?? 0;
    }

    private Process StartProcess(LaunchCommand command, ConsoleColor color, bool noPrefix)
    {
        var process = CreateProcess(command.Label, command.WorkingDirectory, command.FileName, command.Arguments, color, noPrefix);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private Process CreateProcess(string label, string workingDirectory, string fileName, IReadOnlyList<string> arguments, ConsoleColor color, bool noPrefix)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var prefix = noPrefix ? string.Empty : $"[{label}] ";

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) _console.WriteChildLine(prefix, e.Data, color, isError: false); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) _console.WriteChildLine(prefix, e.Data, color, isError: true); };
        return process;
    }

    public async Task<bool> BuildAsync(LaunchPlan plan, LaunchBuildOptions options, CancellationToken cancellationToken)
    {
        if (plan.Commands.Count == 0)
            return true;

        _console.WriteInfo($"Building {plan.Commands.Count} project(s) before launch…");

        for (var i = 0; i < plan.Commands.Count; i++)
        {
            var command = plan.Commands[i];
            var color = _palette[i % _palette.Length];

            var arguments = new List<string> { "build", command.ProjectPath };
            if (!string.IsNullOrWhiteSpace(options.Configuration)) { arguments.Add("--configuration"); arguments.Add(options.Configuration!); }
            if (!string.IsNullOrWhiteSpace(options.Framework)) { arguments.Add("--framework"); arguments.Add(options.Framework!); }
            if (!string.IsNullOrWhiteSpace(options.Verbosity)) { arguments.Add("--verbosity"); arguments.Add(options.Verbosity!); }

            int exitCode;
            try
            {
                exitCode = await BuildOneAsync(command.Label, command.WorkingDirectory, arguments, color, options.NoPrefix, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false; // cancelled — the caller maps this to a clean exit.
            }

            if (exitCode != 0)
            {
                _console.WriteError($"[{command.Label}] build failed (exit code {exitCode}). Not launching.");
                return false;
            }
        }

        return true;
    }

    private async Task<int> BuildOneAsync(string label, string workingDirectory, IReadOnlyList<string> arguments, ConsoleColor color, bool noPrefix, CancellationToken cancellationToken)
    {
        using var process = CreateProcess(label, workingDirectory, "dotnet", arguments, color, noPrefix);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            throw;
        }

        return SafeExitCode(process);
    }

    /// <summary>
    /// Waits for one process to exit on its own. Records the exit code only if no teardown was in progress
    /// (so processes we kill aren't counted as failures), and triggers teardown on a non-zero kill-on-fail exit.
    /// </summary>
    private async Task MonitorAsync(RunningProcess rp, LaunchRunOptions options, CancellationTokenSource teardownCts)
    {
        try { await rp.Process.WaitForExitAsync(); }
        catch (Exception) { /* exited / killed underneath us */ }

        if (teardownCts.IsCancellationRequested)
            return; // killed as part of teardown — not a natural exit.

        var code = SafeExitCode(rp.Process);
        rp.NaturalExitCode = code;

        var message = $"[{rp.Command.Label}] exited with code {code}.";
        if (code == 0) _console.WriteSuccess(message); else _console.WriteError(message);

        if (code != 0 && options.KillOnFail)
            teardownCts.Cancel();
    }

    private async Task TeardownAsync(IReadOnlyList<RunningProcess> running, TimeSpan grace)
    {
        if (running.All(r => SafeHasExited(r.Process)))
            return;

        var aliveCount = running.Count(r => !SafeHasExited(r.Process));
        _console.WriteWarning($"Stopping {aliveCount} process(es)…");

        // Grace window: an interactive Ctrl+C already reached the children via the console group; give
        // them a moment to shut down cleanly before we force the issue.
        await Task.WhenAll(running.Select(r => WaitNoThrow(r.Process, grace)));

        // Force-kill survivors AND their whole subtree. `dotnet run` spawns the app as a child, so killing
        // only the runner would orphan the app — entireProcessTree is the cross-platform guarantee.
        foreach (var rp in running.Where(r => !SafeHasExited(r.Process)))
        {
            try { rp.Process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        }

        await Task.WhenAll(running.Select(r => WaitNoThrow(r.Process, TimeSpan.FromSeconds(10))));
    }

    private static async Task WhenAllOrCancelled(Task allExited, CancellationToken token)
    {
        var cancelled = new TaskCompletionSource();
        using var registration = token.Register(() => cancelled.TrySetResult());
        await Task.WhenAny(allExited, cancelled.Task);
    }

    private static async Task WaitNoThrow(Process process, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cts.Token);
        }
        catch (Exception) { /* timed out or already gone */ }
    }

    private static bool SafeHasExited(Process process)
    {
        try { return process.HasExited; }
        catch (Exception) { return true; }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch (Exception) { return -1; }
    }

    private sealed class RunningProcess
    {
        public RunningProcess(LaunchCommand command, Process process, ConsoleColor color)
        {
            Command = command;
            Process = process;
            Color = color;
        }

        public LaunchCommand Command { get; }
        public Process Process { get; }
        public ConsoleColor Color { get; }

        /// <summary>Exit code if the process exited on its own (not via teardown); otherwise null.</summary>
        public int? NaturalExitCode { get; set; }
    }
}
