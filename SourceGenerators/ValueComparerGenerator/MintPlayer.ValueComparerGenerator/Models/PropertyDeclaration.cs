using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Models;

[ValueComparer(typeof(ValueComparers.PropertyDeclarationValueComparer))]
public class PropertyDeclaration
{
    public string Name { get; set; }
    public string Type { get; set; }
}
