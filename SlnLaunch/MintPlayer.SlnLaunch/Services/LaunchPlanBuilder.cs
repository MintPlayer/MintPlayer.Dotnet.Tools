using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SlnLaunch.Models;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.SlnLaunch.Services;

[Register(typeof(ILaunchPlanBuilder), ServiceLifetime.Singleton, "SlnLaunchServices")]
internal sealed class LaunchPlanBuilder : ILaunchPlanBuilder
{
    public LaunchPlan Build(LaunchProfile profile, string baseDirectory, bool watch)
    {
        var commands = new List<LaunchCommand>();
        var warnings = new List<string>();

        foreach (var entry in profile.Projects)
        {
            if (!entry.ShouldLaunch)
                continue;

            var projectPath = ResolvePath(entry.Path, baseDirectory);

            if (!File.Exists(projectPath))
                throw new SlnLaunchException($"Project not found: '{entry.Path}' (resolved to '{projectPath}').");

            var label = Path.GetFileNameWithoutExtension(projectPath);

            // Docker Compose projects can't be launched via `dotnet run` — out of scope.
            if (projectPath.EndsWith(".dcproj", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Skipping '{label}': Docker Compose (.dcproj) projects are not supported.");
                continue;
            }

            var launchProfile = ResolveLaunchProfile(entry, projectPath, label, warnings);
            var arguments = BuildArguments(projectPath, launchProfile, watch);

            commands.Add(new LaunchCommand(label, "dotnet", projectPath, baseDirectory, arguments, launchProfile));
        }

        return new LaunchPlan(profile.Name, commands, warnings);
    }

    /// <summary>
    /// Decides which launch profile (if any) to pass to <c>dotnet</c>. Passes <c>DebugTarget</c> through
    /// unless it's positively a non-Project profile, in which case it warns and runs without one.
    /// </summary>
    private static string? ResolveLaunchProfile(LaunchProjectEntry entry, string projectPath, string label, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(entry.DebugTarget))
            return null;

        if (LaunchSettingsReader.IsNonProjectProfile(projectPath, entry.DebugTarget!))
        {
            warnings.Add($"'{label}': launch profile '{entry.DebugTarget}' is not a 'Project' profile and can't be run by the dotnet CLI — running without a launch profile.");
            return null;
        }

        return entry.DebugTarget;
    }

    private static IReadOnlyList<string> BuildArguments(string projectPath, string? launchProfile, bool watch)
    {
        var arguments = new List<string>
        {
            watch ? "watch" : "run",
            "--project",
            projectPath,
        };

        if (launchProfile is not null)
        {
            arguments.Add("--launch-profile");
            arguments.Add(launchProfile);
        }

        return arguments;
    }

    private static string ResolvePath(string raw, string baseDirectory)
    {
        var normalized = raw.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.IsPathRooted(normalized) ? normalized : Path.Combine(baseDirectory, normalized);
        return Path.GetFullPath(combined);
    }
}
