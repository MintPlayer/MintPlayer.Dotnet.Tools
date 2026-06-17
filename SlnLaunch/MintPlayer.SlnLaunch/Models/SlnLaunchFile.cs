namespace MintPlayer.SlnLaunch.Models;

/// <summary>
/// A parsed <c>.slnLaunch</c> file together with where it came from. Project paths inside the
/// profiles are relative to <see cref="Directory"/>.
/// </summary>
public sealed class SlnLaunchFile
{
    public SlnLaunchFile(string filePath, IReadOnlyList<LaunchProfile> profiles)
    {
        FilePath = filePath;
        Directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(filePath)) ?? ".";
        Profiles = profiles;
    }

    /// <summary>Absolute path to the <c>.slnLaunch</c> file.</summary>
    public string FilePath { get; }

    /// <summary>Directory the file lives in; the base for resolving relative project paths.</summary>
    public string Directory { get; }

    /// <summary>The launch profiles, in file order.</summary>
    public IReadOnlyList<LaunchProfile> Profiles { get; }
}
