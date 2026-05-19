using System.Xml.Linq;

namespace MintPlayer.Verz.Sdks.Dotnet;

/// <summary>
/// Plain-XML reader for .csproj files. Sufficient for projects that declare
/// PackageId / AssemblyName / TargetFramework as literal text. Property
/// expressions (e.g. <c>$(AssemblyName).Extras</c>) and Directory.Build.props
/// inheritance are not resolved here; upgrading to MSBuild evaluation is a
/// later-phase task.
/// </summary>
internal sealed class CsprojReader
{
    public CsprojReader(string projectFile)
    {
        ProjectFile = projectFile;
        Document = XDocument.Load(projectFile, LoadOptions.PreserveWhitespace);
        Namespace = Document.Root?.Name.Namespace ?? XNamespace.None;
    }

    public string ProjectFile { get; }
    public XDocument Document { get; }
    public XNamespace Namespace { get; }

    public string? GetProperty(string name) =>
        Document.Descendants(Namespace + name)
            .FirstOrDefault(e => !HasCondition(e))?
            .Value?.Trim();

    public string PackageId
    {
        get
        {
            var explicitId = GetProperty("PackageId");
            if (!string.IsNullOrEmpty(explicitId)) return explicitId;

            var assemblyName = GetProperty("AssemblyName");
            if (!string.IsNullOrEmpty(assemblyName)) return assemblyName;

            return Path.GetFileNameWithoutExtension(ProjectFile);
        }
    }

    public IReadOnlyList<string> TargetFrameworks
    {
        get
        {
            var single = GetProperty("TargetFramework");
            if (!string.IsNullOrEmpty(single))
                return new[] { single };

            var multi = GetProperty("TargetFrameworks");
            if (string.IsNullOrEmpty(multi))
                return Array.Empty<string>();

            return multi
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }
    }

    public bool IsPackable
    {
        get
        {
            var value = GetProperty("IsPackable");
            // Default is true. Only false if the property is set to "false" literally.
            return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string? OutputType => GetProperty("OutputType");

    public bool IsLibrary
    {
        get
        {
            var output = OutputType;
            // Library is the implicit default for Microsoft.NET.Sdk. Treat null/empty as Library.
            if (string.IsNullOrEmpty(output)) return true;
            return string.Equals(output, "Library", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool HasCondition(XElement e) =>
        e.Attribute("Condition") is not null;
}
