using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools;

public sealed class SettingsValueComparer : ValueComparer<Settings>
{
    protected override bool AreEqual(Settings x, Settings y)
    {
        if (!IsEquals(x.RootNamespace, y.RootNamespace)) return false;
        if (!IsEquals(x.IncrementalValueProviderSymbol, y.IncrementalValueProviderSymbol)) return false;
        return true;
    }
}
