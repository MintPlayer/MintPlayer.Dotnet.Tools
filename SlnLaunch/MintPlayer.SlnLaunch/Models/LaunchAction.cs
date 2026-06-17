namespace MintPlayer.SlnLaunch.Models;

/// <summary>
/// What a <c>.slnLaunch</c> profile should do with a project. The string tokens match
/// exactly what Visual Studio serializes (<c>Start</c>, <c>StartWithoutDebugging</c>);
/// <c>None</c> is the default so that a missing or omitted action is treated as "skip"
/// rather than accidentally launching the project.
/// </summary>
public enum LaunchAction
{
    /// <summary>Do not launch this project. Default when the action is absent.</summary>
    None = 0,

    /// <summary>Launch the project (Visual Studio's F5 / "Start").</summary>
    Start,

    /// <summary>Launch the project without a debugger (Visual Studio's Ctrl+F5).</summary>
    StartWithoutDebugging,
}
