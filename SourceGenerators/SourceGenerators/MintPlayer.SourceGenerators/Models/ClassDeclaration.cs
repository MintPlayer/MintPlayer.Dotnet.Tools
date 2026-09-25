using Microsoft.CodeAnalysis;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[GenerateEquality]
public partial class ClassDeclaration
{
    public string? Name { get; set; }
}
