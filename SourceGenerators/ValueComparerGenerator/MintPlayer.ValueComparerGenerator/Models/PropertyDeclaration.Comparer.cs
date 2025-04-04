using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.ValueComparerGenerator.Models;

public class PropertyDeclarationValueComparer : ValueComparer<PropertyDeclaration>
{
    protected override bool AreEqual(PropertyDeclaration x, PropertyDeclaration y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Type, y.Type)) return false;
        if (!IsEquals(x.HasComparerIgnore, y.HasComparerIgnore)) return false;

        return true;
    }
}
