using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.ValueComparers;

public class FieldDeclarationComparer : ValueComparer<FieldDeclaration>
{
    protected override bool AreEqual(FieldDeclaration x, FieldDeclaration y)
    {
        if (!IsEquals(x.FullyQualifiedTypeName, y.FullyQualifiedTypeName)) return false;
        if (!IsEquals(x.FullyQualifiedClassName, y.FullyQualifiedClassName)) return false;
        if (!IsEquals(x.Type, y.Type)) return false;
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.ClassName, y.ClassName)) return false;
        if (!IsEquals(x.Namespace, y.Namespace)) return false;

        return true;
    }
}
