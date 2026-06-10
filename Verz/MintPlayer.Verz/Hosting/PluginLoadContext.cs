using System.Reflection;
using System.Runtime.Loader;

namespace MintPlayer.Verz.Hosting;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// Assemblies that must resolve to the host's default ALC even when the
    /// plugin's package directory contains a copy. These types cross the
    /// plugin/host boundary; identity-mismatch would break casts and DI.
    /// </summary>
    public static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.Ordinal)
    {
        "MintPlayer.Verz.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "NuGet.Versioning",
    };

    public static bool IsShared(string assemblyName) => SharedAssemblyNames.Contains(assemblyName);

    private readonly IReadOnlyList<string> _searchDirs;

    public PluginLoadContext(string mainAssemblyPath, IReadOnlyList<string> searchDirs)
        : base(name: Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: false)
    {
        _searchDirs = searchDirs;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null) return null;
        if (SharedAssemblyNames.Contains(assemblyName.Name)) return null;

        foreach (var dir in _searchDirs)
        {
            var candidate = Path.Combine(dir, assemblyName.Name + ".dll");
            if (File.Exists(candidate)) return LoadFromAssemblyPath(candidate);
        }
        return null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var dir in _searchDirs)
        {
            var candidate = Path.Combine(dir, unmanagedDllName);
            if (File.Exists(candidate)) return LoadUnmanagedDllFromPath(candidate);
        }
        return nint.Zero;
    }
}
