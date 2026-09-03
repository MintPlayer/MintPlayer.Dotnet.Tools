using System.Xml.Linq;
using Microsoft.Build.Framework;

/// <summary>
/// Writes the computed public-API hash into the nuspec's <c>&lt;metadata&gt;</c> so the published
/// package records the surface it shipped.
/// </summary>
/// <remarks>
/// On failure this returns <see langword="false"/> and fails the build. It used to return
/// <see langword="true"/> from every path, on the reasoning that a hash problem should not break
/// pack — but that turns "the package went out without its API hash" into a silent event, which is
/// the one outcome the task exists to prevent. A pack that cannot record what it shipped is a pack
/// worth stopping.
///
/// The one genuinely non-fatal case is kept and narrowed: no nuspec at all means this project is
/// not producing a package on this build, so there is nothing to inject and nothing wrong.
/// </remarks>
public class InjectPublicApiHashTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string NuspecPath { get; set; } = string.Empty;

    [Required]
    public string PublicApiHash { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            // Not packing on this build — nothing to do, and not an error.
            if (string.IsNullOrWhiteSpace(NuspecPath) || !File.Exists(NuspecPath))
            {
                Log.LogMessage(MessageImportance.Low, $"Nuspec not found, nothing to inject: {NuspecPath}");
                return true;
            }

            if (string.IsNullOrWhiteSpace(PublicApiHash))
            {
                Log.LogError($"PublicApiHash is empty; refusing to write a blank hash into {NuspecPath}.");
                return false;
            }

            var doc = XDocument.Load(NuspecPath);
            var ns = doc.Root?.Name.Namespace;
            var metadata = doc.Root?.Element(ns + "metadata");
            if (metadata == null)
            {
                // A nuspec exists but is not shaped like one. Injecting is impossible and the
                // package would ship without the hash, so this is a failure, not a warning.
                Log.LogError($"Nuspec has no <metadata> element, cannot inject PublicApiHash: {NuspecPath}");
                return false;
            }

            var existing = metadata.Element(ns + "PublicApiHash");
            if (existing == null)
            {
                metadata.Add(new XElement(ns + "PublicApiHash", PublicApiHash));
            }
            else
            {
                existing.Value = PublicApiHash;
            }

            doc.Save(NuspecPath);
            Log.LogMessage(MessageImportance.Low, $"Injected PublicApiHash into nuspec: {PublicApiHash}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to inject PublicApiHash into {NuspecPath}: {ex.Message}");
            return false;
        }
    }
}
