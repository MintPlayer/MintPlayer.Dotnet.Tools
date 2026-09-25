using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[GenerateEquality]
public partial class GenericMethodDeclaration
{
    public MethodDeclaration? Method { get; set; }
    public int Count { get; set; }
}
