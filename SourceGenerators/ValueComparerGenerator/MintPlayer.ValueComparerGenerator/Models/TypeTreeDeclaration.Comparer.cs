using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.ValueComparerGenerator.Models;

public class TypeTreeDeclarationComparer : ValueComparer<TypeTreeDeclaration>
{
    protected override bool AreEqual(TypeTreeDeclaration x, TypeTreeDeclaration y)
    {
        if (!IsEquals(x.BaseType, y.BaseType)) return false;
        if (!IsEquals(x.DerivedTypes, y.DerivedTypes)) return false;
        return true;
    }
}

public class DerivedTypeValueComparer : ValueComparer<DerivedType>
{
    protected override bool AreEqual(DerivedType x, DerivedType y)
    {
        if (!IsEquals(x.Type, y.Type)) return false;
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.PathSpec, y.PathSpec)) return false;
        if (!IsEquals(x.AllProperties, y.AllProperties)) return false;
        return true;
    }
}

public class BaseTypeValueComparer : ValueComparer<BaseType>
{
    protected override bool AreEqual(BaseType x, BaseType y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.FullName, y.FullName)) return false;
        if (!IsEquals(x.IsPartial, y.IsPartial)) return false;
        if (!IsEquals(x.PathSpec, y.PathSpec)) return false;
        if (!IsEquals(x.Properties, y.Properties)) return false;
        if (!IsEquals(x.AllProperties, y.AllProperties)) return false;
        if (!IsEquals(x.HasAttribute, y.HasAttribute)) return false;
        return true;
    }
}
