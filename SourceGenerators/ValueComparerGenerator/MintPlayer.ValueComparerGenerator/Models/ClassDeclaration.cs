using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Models;

[ValueComparer(typeof(ClassDeclarationValueComparer))]
public class ClassDeclaration
{
    public string? Name { get; set; }
    public string? FullName { get; set; }
    public string? Namespace { get; set; }
    public bool IsPartial { get; set; }
    public bool IsAbstract { get; set; }
    public bool HasAutoValueComparerAttribute { get; set; }
    public PropertyDeclaration[] Properties { get; set; } = [];
    public PropertyDeclaration[] AllProperties { get; set; } = [];
    public Location? Location { get; internal set; }

    public override string ToString() => FullName ?? string.Empty;
}
