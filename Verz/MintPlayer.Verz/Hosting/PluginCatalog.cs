using MintPlayer.Verz.Abstractions;

namespace MintPlayer.Verz.Hosting;

public sealed class PluginCatalog
{
    public PluginCatalog(IReadOnlyList<IDevelopmentSdk> sdks, IReadOnlyList<IPackageRegistry> registries)
    {
        Sdks = sdks;
        Registries = registries;
    }

    public IReadOnlyList<IDevelopmentSdk> Sdks { get; }
    public IReadOnlyList<IPackageRegistry> Registries { get; }
}
