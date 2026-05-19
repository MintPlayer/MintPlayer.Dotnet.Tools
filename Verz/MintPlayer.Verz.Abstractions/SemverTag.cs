using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace MintPlayer.Verz.Abstractions;

public sealed record SemverTag(string PackageId, NuGetVersion Version)
{
    private static readonly Regex TagShape = new(
        @"^(?<id>.+)/v(?<ver>\d+\.\d+\.\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string TagName => $"{PackageId}/v{Version.ToNormalizedString()}";

    public static bool TryParse(string tag, [NotNullWhen(true)] out SemverTag? result)
    {
        result = null;
        if (string.IsNullOrEmpty(tag)) return false;

        var match = TagShape.Match(tag);
        if (!match.Success) return false;

        if (!NuGetVersion.TryParse(match.Groups["ver"].Value, out var version)) return false;
        if (version.IsPrerelease) return false;

        result = new SemverTag(match.Groups["id"].Value, version);
        return true;
    }
}
