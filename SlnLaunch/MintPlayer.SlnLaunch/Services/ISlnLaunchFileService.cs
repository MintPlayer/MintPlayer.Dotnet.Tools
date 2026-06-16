using MintPlayer.SlnLaunch.Models;

namespace MintPlayer.SlnLaunch.Services;

/// <summary>
/// Locates and parses <c>.slnLaunch</c> files.
/// </summary>
public interface ISlnLaunchFileService
{
    /// <summary>
    /// Finds the launch files in <paramref name="directory"/>, in precedence order:
    /// <c>*.slnLaunch</c>, then <c>*.slnLaunch.user</c>, then <c>*.slnxLaunch</c>. Only the
    /// highest-precedence tier that has any matches is returned; an empty list means none were found.
    /// More than one match in that tier is ambiguous — the caller decides how to report it.
    /// </summary>
    IReadOnlyList<string> Find(string directory);

    /// <summary>
    /// Parses the file at <paramref name="path"/>.
    /// </summary>
    /// <exception cref="SlnLaunchException">The file is missing, malformed, or has no usable profiles.</exception>
    SlnLaunchFile Load(string path);
}
