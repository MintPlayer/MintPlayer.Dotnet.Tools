using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Models;

[ValueComparer(typeof(ValueComparers.ClassDeclarationValueComparer))]
public class ClassDeclaration
{
    public string? Name { get; set; }
    public string? FullName { get; set; }
    public string? Namespace { get; set; }
    public bool IsPartial { get; set; }
    public bool HasAutoValueComparerAttribute { get; set; }
    public PropertyDeclaration[] Properties { get; set; } = [];
    public Location? Location { get; internal set; }
}
