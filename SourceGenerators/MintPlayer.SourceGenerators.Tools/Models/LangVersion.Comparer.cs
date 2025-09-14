using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools.Models;

internal class LangVersionComparer : ValueComparer<LangVersion>
{
    protected override bool AreEqual(LangVersion x, LangVersion y)
    {
        if (!IsEquals(x.LanguageVersion, y.LanguageVersion)) return false;
        if (!IsEquals(x.Weight, y.Weight)) return false;

        return base.AreEqual(x, y);
    }
}
