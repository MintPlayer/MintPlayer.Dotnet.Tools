namespace MintPlayer.SlnLaunch.Models;

/// <summary>
/// A fully resolved command to start one project: the exact <c>dotnet</c> invocation, where to run
/// it, and a short label for prefixing its output.
/// </summary>
public sealed class LaunchCommand
{
    public LaunchCommand(string label, string projectPath, string workingDirectory, IReadOnlyList<string> arguments, string? launchProfile)
    {
        Label = label;
        ProjectPath = projectPath;
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
        LaunchProfile = launchProfile;
    }

    /// <summary>Short, human-friendly name (the project file name without extension) used to prefix output.</summary>
    public string Label { get; }

    /// <summary>Absolute path to the project file.</summary>
    public string ProjectPath { get; }

    /// <summary>Directory the process is started in (the solution directory).</summary>
    public string WorkingDirectory { get; }

    /// <summary>The executable to run — always <c>dotnet</c>.</summary>
    public string FileName => "dotnet";

    /// <summary>Arguments passed to <c>dotnet</c> (e.g. <c>run --project … --launch-profile …</c>).</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>The launch profile actually used, or <c>null</c> when running without one.</summary>
    public string? LaunchProfile { get; }

    /// <summary>The full command line as it would appear in a shell (for <c>--dry-run</c>).</summary>
    public string ToDisplayString()
        => "dotnet " + string.Join(" ", Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
