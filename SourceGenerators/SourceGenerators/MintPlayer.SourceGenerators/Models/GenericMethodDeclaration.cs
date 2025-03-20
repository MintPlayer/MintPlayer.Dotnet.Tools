using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[AutoValueComparer]
public partial class GenericMethodDeclaration
{
    public MethodDeclaration? Method { get; set; }
    public int Count { get; set; }
}
