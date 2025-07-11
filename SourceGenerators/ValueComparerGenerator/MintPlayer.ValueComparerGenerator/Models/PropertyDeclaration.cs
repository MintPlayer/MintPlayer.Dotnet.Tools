using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Models;

[ValueComparer(typeof(PropertyDeclarationValueComparer))]
public class PropertyDeclaration
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool HasComparerIgnore { get; set; }

    public override string ToString() => $"{Type} {Name}";
}
