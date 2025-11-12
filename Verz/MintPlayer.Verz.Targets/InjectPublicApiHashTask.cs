using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class InjectPublicApiHashTask : Task
{
    [Required]
    public string NuspecPath { get; set; } = string.Empty;

    [Required]
    public string PublicApiHash { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NuspecPath) || !File.Exists(NuspecPath))
            {
                Log.LogMessage(MessageImportance.Low, $"Nuspec not found: {NuspecPath}");
                return true; // non-fatal
            }

            var doc = XDocument.Load(NuspecPath);
            var ns = doc.Root?.Name.Namespace;
            var metadata = doc.Root?.Element(ns + "metadata");
            if (metadata == null)
            {
                Log.LogWarning($"Nuspec missing <metadata>: {NuspecPath}");
                return true;
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
            Log.LogWarning($"Failed to inject PublicApiHash: {ex.Message}");
            return true; // do not break pack
        }
    }
}

