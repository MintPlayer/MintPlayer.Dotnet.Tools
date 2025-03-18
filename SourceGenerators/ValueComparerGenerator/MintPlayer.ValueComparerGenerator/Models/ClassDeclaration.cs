using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Models;

[ValueComparer(typeof(ValueComparers.ClassDeclarationValueComparer))]
public class ClassDeclaration
{
    public string? Name { get; set; }
    public string? FullName { get; set; }
    public string? Namespace { get; set; }
    public string ComparerType { get; set; }
    public string ComparerAttributeType { get; set; }
    public PropertyDeclaration[] Properties { get; set; } = [];
}
