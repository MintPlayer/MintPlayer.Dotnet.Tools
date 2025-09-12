using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[AutoValueComparer]
public partial class ClassDeclaration
{
    public string? Name { get; set; }
    public string? FullyQualifiedName { get; set; }
    public string? Namespace { get; set; }
    public ConversionMethod[] ConversionMethods { get; set; } = [];
}
