using System.Reflection;
using System.Runtime.Loader;

namespace MintPlayer.Verz.Hosting;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.Ordinal)
    {
        "MintPlayer.Verz.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "NuGet.Versioning",
    };

    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string mainAssemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null) return null;
        if (SharedAssemblyNames.Contains(assemblyName.Name)) return null;
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
