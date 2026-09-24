using MintPlayer.SourceGenerators.Tools.Polyfills;
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
        if (!IsEquals(x.IsInternal, y.IsInternal)) return false;
        // IsAbstract and AllProperties were hashed but not compared, although the producer emits from
        // both: a class that inherited a new property compared equal and kept its stale comparer.
        if (!IsEquals(x.IsAbstract, y.IsAbstract)) return false;
        if (!IsEquals(x.HasAutoValueComparerAttribute, y.HasAutoValueComparerAttribute)) return false;
        if (!IsEquals(x.Properties, y.Properties)) return false;
        if (!IsEquals(x.AllProperties, y.AllProperties)) return false;

        return true;
    }

    protected override void AddHash(ref HashCodeCompat h, ClassDeclaration? obj)
    {
        AddHash(ref h, obj?.Name);
        AddHash(ref h, obj?.FullName);
        AddHash(ref h, obj?.PathSpec);
        AddHash(ref h, obj?.IsPartial);
        AddHash(ref h, obj?.IsInternal);
        AddHash(ref h, obj?.IsAbstract);
        AddHash(ref h, obj?.HasAutoValueComparerAttribute);
        AddHash(ref h, obj?.Properties);
        AddHash(ref h, obj?.AllProperties);
    }
}
