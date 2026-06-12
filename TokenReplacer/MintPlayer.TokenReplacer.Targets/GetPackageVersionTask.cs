using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MintPlayer.TokenReplacer.Targets;

/// <summary>
/// MSBuild task that looks up the <em>resolved</em> version of NuGet packages in the project's
/// <c>project.assets.json</c> — i.e. the version restore actually picked, not the requested range.
/// </summary>
public class GetPackageVersionTask : Microsoft.Build.Utilities.Task
{
    /// <summary>Path to the project's assets file (<c>$(ProjectAssetsFile)</c>).</summary>
    [Required]
    public string AssetsFile { get; set; } = "";

    /// <summary>
    /// Packages to look up: the item spec is the package id. Optional <c>TokenName</c> metadata
    /// sets the name of the token the version is registered under (defaults to the package id).
    /// </summary>
    [Required]
    public ITaskItem[] Packages { get; set; } = [];

    /// <summary>
    /// One item per requested package: item spec is the token name, with <c>Value</c> (the resolved
    /// version, ready to consume as a TokenReplaceValue item), <c>Version</c> and <c>PackageId</c> metadata.
    /// </summary>
    [Output]
    public ITaskItem[] ResolvedPackages { get; private set; } = [];

    /// <inheritdoc/>
    public override bool Execute()
    {
        if (!File.Exists(AssetsFile))
        {
            Log.LogError(null, "MPTR004", null, null, 0, 0, 0, 0,
                $"NuGet assets file not found: '{AssetsFile}'. Make sure restore has run before the token replacement targets.");
            return false;
        }

        Dictionary<string, string> versions;
        try
        {
            versions = AssetsFileVersionReader.ReadLibraryVersions(File.ReadAllText(AssetsFile));
        }
        catch (FormatException ex)
        {
            Log.LogError(null, "MPTR004", null, AssetsFile, 0, 0, 0, 0,
                $"Could not parse NuGet assets file '{AssetsFile}': {ex.Message}");
            return false;
        }

        var resolved = new List<ITaskItem>();
        foreach (var package in Packages)
        {
            var packageId = package.ItemSpec;
            if (!versions.TryGetValue(packageId, out var version))
            {
                Log.LogError(null, "MPTR001", null, AssetsFile, 0, 0, 0, 0,
                    $"Package '{packageId}' was not found in '{AssetsFile}'. Is it referenced (directly or transitively) by this project?");
                continue;
            }

            var tokenName = package.GetMetadata("TokenName");
            if (string.IsNullOrEmpty(tokenName))
                tokenName = packageId;

            var item = new TaskItem(tokenName);
            item.SetMetadata("Value", version);
            item.SetMetadata("Version", version);
            item.SetMetadata("PackageId", packageId);
            resolved.Add(item);

            Log.LogMessage(MessageImportance.Low, $"Resolved package version token '{tokenName}' = {version} ({packageId}).");
        }

        ResolvedPackages = resolved.ToArray();
        return !Log.HasLoggedErrors;
    }
}
