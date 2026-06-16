namespace MintPlayer.SlnLaunch.Models;

/// <summary>
/// The resolved set of commands to launch for a profile, plus any warnings produced while building it
/// (e.g. a non-Project launch profile that was downgraded, or a skipped Docker Compose project).
/// </summary>
public sealed class LaunchPlan
{
    public LaunchPlan(string profileName, IReadOnlyList<LaunchCommand> commands, IReadOnlyList<string> warnings)
    {
        ProfileName = profileName;
        Commands = commands;
        Warnings = warnings;
    }

    public string ProfileName { get; }

    /// <summary>Commands to launch, one per project that should start.</summary>
    public IReadOnlyList<LaunchCommand> Commands { get; }

    /// <summary>Non-fatal issues to surface to the user before launching.</summary>
    public IReadOnlyList<string> Warnings { get; }
}
