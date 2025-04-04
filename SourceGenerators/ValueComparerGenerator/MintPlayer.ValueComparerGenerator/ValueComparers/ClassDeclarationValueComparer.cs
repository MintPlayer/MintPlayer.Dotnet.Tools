using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.ValueComparerGenerator.Models;

namespace MintPlayer.ValueComparerGenerator.ValueComparers;

public class ClassDeclarationValueComparer : ValueComparer<ClassDeclaration>
{
    protected override bool AreEqual(ClassDeclaration x, ClassDeclaration y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.FullName, y.FullName)) return false;
        if (!IsEquals(x.Namespace, y.Namespace)) return false;
        if (!IsEquals(x.IsPartial, y.IsPartial)) return false;
        if (!IsEquals(x.HasAutoValueComparerAttribute, y.HasAutoValueComparerAttribute)) return false;
        if (!IsEquals(x.Properties, y.Properties)) return false;

        return true;
    }
}
