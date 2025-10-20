using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.ValueComparerGenerator.Models;

public class ClassDeclarationValueComparer : ValueComparer<ClassDeclaration>
{
    protected override bool AreEqual(ClassDeclaration x, ClassDeclaration y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.FullName, y.FullName)) return false;
        if (!IsEquals(x.PathSpec, y.PathSpec)) return false;
        if (!IsEquals(x.IsPartial, y.IsPartial)) return false;
        if (!IsEquals(x.HasAutoValueComparerAttribute, y.HasAutoValueComparerAttribute)) return false;
        if (!IsEquals(x.Properties, y.Properties)) return false;

        return true;
    }

    protected override void AddHash(ref HashCode h, ClassDeclaration? obj)
    {
        AddHash(ref h, obj?.Name);
        AddHash(ref h, obj?.FullName);
        AddHash(ref h, obj?.PathSpec);
        AddHash(ref h, obj?.IsPartial);
        AddHash(ref h, obj?.IsAbstract);
        AddHash(ref h, obj?.HasAutoValueComparerAttribute);
        AddHash(ref h, obj?.Properties);
        AddHash(ref h, obj?.AllProperties);
        AddHash(ref h, obj?.Location);
    }
}
