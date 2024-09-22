namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

public sealed class SettingsValueComparer : ValueComparer<Settings>
{
    protected override bool AreEqual(Settings x, Settings y)
    {
        if (!IsEquals(x.RootNamespace, y.RootNamespace))
            return false;

        return true;
    }
}
