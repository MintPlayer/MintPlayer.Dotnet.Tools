using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[AutoValueComparer]
public partial class InjectField
{
    public string? Type { get; set; }
    public string? Name { get; set; }
}
