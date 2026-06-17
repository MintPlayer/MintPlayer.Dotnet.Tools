namespace MintPlayer.SlnLaunch.Models;

/// <summary>
/// A named multi-project launch profile — one entry of the top-level array in a
/// <c>.slnLaunch</c> file. Shown in Visual Studio's startup dropdown.
/// </summary>
public sealed class LaunchProfile
{
    /// <summary>Profile name (the dropdown entry / the value selected with <c>--profile</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The projects this profile launches.</summary>
    public List<LaunchProjectEntry> Projects { get; set; } = [];
}
