//using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

// [AutoValueComparer]
public partial class FieldDeclaration
{
    public string? Name { get; set; }
    public string? FullyQualifiedClassName { get; set; }
    public string? ClassName { get; set; }
    public string? Namespace { get; set; }
    public string? FullyQualifiedTypeName { get; set; }
    public string? Type { get; set; }
}
