using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.ValueComparerGenerator.Models;

namespace MintPlayer.ValueComparerGenerator.ValueComparers;

public class PropertyDeclarationValueComparer : ValueComparer<PropertyDeclaration>
{
    protected override bool AreEqual(PropertyDeclaration x, PropertyDeclaration y)
    {
        return true;
    }
}
