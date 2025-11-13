using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.Verz.Core;

namespace MintPlayer.Verz.Helpers;

internal sealed partial class ToolCatalogResult
{
    [Inject] public IReadOnlyList<IPackageRegistry> Registries { get; }
    [Inject] public IReadOnlyList<IDevelopmentSdk> Sdks { get; }
}