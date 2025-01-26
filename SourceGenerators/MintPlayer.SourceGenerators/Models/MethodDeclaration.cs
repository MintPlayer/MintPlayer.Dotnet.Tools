using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Models;

public class MethodDeclaration
{
    public string? MethodName { get; set; }
    public string? ClassName { get; set; }
    public string? ContainingNamespace { get; set; }
    public SyntaxTokenList MethodModifiers { get; set; }
    public SyntaxTokenList ClassModifiers { get; set; }
    //public bool ClassIsPartial { get; set; }
    //public bool MethodIsPartial { get; set; }
    //public bool ClassIsStatic { get; set; }
    //public bool MethodIsStatic { get; set; }
    //public Generators.GenericMethodAttribute? GenericMethodAttribute { get; set; }
}
