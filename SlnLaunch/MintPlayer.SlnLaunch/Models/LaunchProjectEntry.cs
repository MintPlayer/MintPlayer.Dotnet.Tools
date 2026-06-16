using System.Text.Json.Serialization;

namespace MintPlayer.SlnLaunch.Models;

/// <summary>
/// A single project entry inside a <c>.slnLaunch</c> profile.
/// </summary>
public sealed class LaunchProjectEntry
{
    /// <summary>
    /// Project path as written in the file: relative to the solution (the <c>.slnLaunch</c>
    /// directory) and backslash-separated. Resolved and normalized later by the plan builder.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>What to do with the project. See <see cref="LaunchAction"/>.</summary>
    public LaunchAction Action { get; set; } = LaunchAction.None;

    /// <summary>
    /// The launch target to start the project with — a profile name from the project's
    /// <c>Properties/launchSettings.json</c> (e.g. <c>https</c>). Absent means "use the default".
    /// </summary>
    public string? DebugTarget { get; set; }

    /// <summary>True when this entry should actually be launched.</summary>
    [JsonIgnore]
    public bool ShouldLaunch => Action is LaunchAction.Start or LaunchAction.StartWithoutDebugging;
}
