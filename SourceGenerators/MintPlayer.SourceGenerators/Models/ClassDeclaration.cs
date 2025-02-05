using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.ValueComparers;

namespace MintPlayer.SourceGenerators.Models;

[ValueComparer(typeof(ClassDeclarationValueComparer))]
public class ClassDeclaration
{
    public string? Name { get; set; }
}
