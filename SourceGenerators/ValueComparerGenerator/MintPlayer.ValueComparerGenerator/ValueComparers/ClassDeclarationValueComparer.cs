using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.ValueComparerGenerator.Models;

namespace MintPlayer.ValueComparerGenerator.ValueComparers;

public class ClassDeclarationValueComparer : ValueComparer<ClassDeclaration>
{
    protected override bool AreEqual(ClassDeclaration x, ClassDeclaration y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;

        return true;
    }
}
