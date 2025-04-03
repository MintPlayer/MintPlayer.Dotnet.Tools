namespace MintPlayer.ValueComparerGenerator.Models;

public class TypeTreeDeclaration
{
    public BaseType BaseType { get; set; } = null!;
    public DerivedType[] DerivedTypes { get; set; } = [];
    //public string ComparerType { get; set; }
    //public string ComparerAttributeType { get; set; }
    //public string BaseTypeName { get; set; }
    //public bool IsBaseTypePartial { get; set; }
}

public class DerivedType
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Namespace { get; set; }
}

public class BaseType
{
    public string? Name { get; set; }
    public string? FullName { get; set; }
    public bool IsPartial { get; set; }
    public string? Namespace { get; set; }
}
