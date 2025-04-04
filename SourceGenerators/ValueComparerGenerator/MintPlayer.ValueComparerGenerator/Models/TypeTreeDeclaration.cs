using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Models;

[ValueComparer(typeof(TypeTreeDeclarationComparer))]
public class TypeTreeDeclaration
{
    public BaseType BaseType { get; set; } = null!;
    public DerivedType[] DerivedTypes { get; set; } = [];

    public override string ToString() => $"Tree: {BaseType}";
}

[ValueComparer(typeof(DerivedTypeValueComparer))]
public class DerivedType
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Namespace { get; set; }

    public override string ToString() => Type ?? string.Empty;
}

[ValueComparer(typeof(BaseTypeValueComparer))]
public class BaseType
{
    public string? Name { get; set; }
    public string? FullName { get; set; }
    public bool IsPartial { get; set; }
    public string? Namespace { get; set; }
    public PropertyDeclaration[] Properties { get; set; } = [];
    public bool HasAttribute { get; set; }

    public override string ToString() => FullName ?? string.Empty;
}