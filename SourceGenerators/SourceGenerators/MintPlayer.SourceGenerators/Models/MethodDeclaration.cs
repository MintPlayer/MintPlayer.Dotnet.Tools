using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[AutoValueComparer]
public partial class MethodDeclaration
{
    public string? MethodName { get; set; }
    public string? ClassName { get; set; }
    public string? ContainingNamespace { get; set; }
    public PathSpec? PathSpec { get; set; }
    public SyntaxTokenList MethodModifiers { get; set; }
    public SyntaxTokenList ClassModifiers { get; set; }
    //public bool ClassIsPartial { get; set; }
    //public bool MethodIsPartial { get; set; }
    //public bool ClassIsStatic { get; set; }
    //public bool MethodIsStatic { get; set; }
    //public Generators.GenericMethodAttribute? GenericMethodAttribute { get; set; }
}
