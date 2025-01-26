using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.ValueComparers;

internal class InjectFieldValueComparer : ValueComparer<InjectField>
{
    protected override bool AreEqual(InjectField x, InjectField y)
    {
        if (!IsEquals(x.Type, y.Type)) return false;
        if (!IsEquals(x.Name, y.Name)) return false;

        return true;
    }
}
