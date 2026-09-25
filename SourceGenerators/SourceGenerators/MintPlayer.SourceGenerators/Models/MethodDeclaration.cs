using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[GenerateEquality]
public partial class MethodDeclaration
{
    public string? MethodName { get; set; }
    public string? ClassName { get; set; }
    public string? ContainingNamespace { get; set; }
    public PathSpec? PathSpec { get; set; }

    // Plain flags rather than the declarations' SyntaxTokenLists. A token list compares by syntax
    // identity, and every re-parse of the file produces new tokens, so a model holding one was
    // never equal to its previous run. These are the only modifiers the producer asks about.
    public bool ClassIsPartial { get; set; }
    public bool ClassIsStatic { get; set; }
    public bool MethodIsPrivate { get; set; }
    public bool MethodIsPartial { get; set; }
    public bool MethodIsStatic { get; set; }
}
