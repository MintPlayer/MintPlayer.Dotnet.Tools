using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using System.Xml.Linq;

namespace MintPlayer.LocalPackagePublisher;

public interface INugetConfigResolver
{
    public IDictionary<string, string> LoadPackageSources(string startDir);
}

[Register(typeof(INugetConfigResolver), ServiceLifetime.Transient)]
internal class NugetConfigResolver : INugetConfigResolver
{
    public IDictionary<string, string> LoadPackageSources(string startDir)
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var userConfig in GetUserConfigs())
            Merge(userConfig, sources);

        foreach (var config in EnumerateConfigs(startDir))
            Merge(config, sources);

        return sources;
    }

    private void Merge(string path, IDictionary<string, string> sources)
    {
        if (!File.Exists(path)) return;

        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch { return; }

        var root = doc.Root;
        if (root?.Name.LocalName != "configuration") return;

        var packageSources = root.Element("packageSources")?.Elements("add");
        if (packageSources == null) return;

        var baseDir = Path.GetDirectoryName(path)!;

        foreach (var add in packageSources)
        {
            var key = (string?)add.Attribute("key");
            var val = (string?)add.Attribute("value");
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(val)) continue;

            if (!Uri.TryCreate(val, UriKind.Absolute, out var uri) ||
                uri.Scheme == Uri.UriSchemeFile)
            {
                var full = val;
                if (!Path.IsPathRooted(val))
                    full = Path.GetFullPath(Path.Combine(baseDir, val));

                sources[key] = full;
            }
            else
            {
                sources[key] = val;
            }
        }
    }

    private IEnumerable<string> EnumerateConfigs(string startDir)
    {
        var dirs = new List<string>();
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            dirs.Add(dir.FullName);
            dir = dir.Parent;
        }

        dirs.Reverse();

        foreach (var d in dirs)
        {
            var lower = Path.Combine(d, "nuget.config");
            var upper = Path.Combine(d, "NuGet.Config");

            if (File.Exists(lower)) yield return lower;
            if (File.Exists(upper) && !upper.Equals(lower, StringComparison.OrdinalIgnoreCase))
                yield return upper;
        }
    }

    private IEnumerable<string> GetUserConfigs()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            var win = Path.Combine(appData, "NuGet", "NuGet.Config");
            if (File.Exists(win))
                yield return win;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            var nix = Path.Combine(home, ".nuget", "NuGet", "NuGet.Config");
            if (File.Exists(nix))
                yield return nix;
        }
    }
}
