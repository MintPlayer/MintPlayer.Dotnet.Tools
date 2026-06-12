using System.Diagnostics;
using System.Reflection;

namespace MintPlayer.TokenReplacer.Tests.Integration;

/// <summary>
/// Runs the <c>dotnet</c> CLI against fixture projects in a temp directory.
/// </summary>
internal static class MsBuildRunner
{
    /// <summary>Source directory of the MintPlayer.TokenReplacer.Targets project (from assembly metadata).</summary>
    public static string TokenReplacerProjectDir { get; } = Path.GetFullPath(
        typeof(MsBuildRunner).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "TokenReplacerProjectDir").Value!);

    /// <summary>The task assembly, copied to the test output by the ProjectReference.</summary>
    public static string TasksAssemblyPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "MintPlayer.TokenReplacer.Targets.dll");

    public static string PropsPath { get; } = Path.Combine(TokenReplacerProjectDir, "MintPlayer.TokenReplacer.Targets.props");
    public static string TargetsPath { get; } = Path.Combine(TokenReplacerProjectDir, "MintPlayer.TokenReplacer.Targets.targets");

    public static string CreateTempWorkspace()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mptr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void TryDeleteWorkspace(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { /* best effort; temp dir */ }
        catch (UnauthorizedAccessException) { /* best effort; temp dir */ }
    }

    /// <summary>Forward slashes so the path can be embedded in XML/MSBuild on any OS.</summary>
    public static string Slashed(string path) => path.Replace('\\', '/');

    public static (int ExitCode, string Output) RunDotnet(string workingDirectory, string arguments, IDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        if (environment != null)
        {
            foreach (var pair in environment)
                psi.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5 * 60 * 1000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {arguments} did not finish within 5 minutes.");
        }
        return (process.ExitCode, stdout.Result + Environment.NewLine + stderr.Result);
    }

    public static void AssertBuildSucceeded((int ExitCode, string Output) result)
    {
        Assert.True(result.ExitCode == 0, $"Expected the build to succeed, but it exited with {result.ExitCode}:{Environment.NewLine}{result.Output}");
    }
}
