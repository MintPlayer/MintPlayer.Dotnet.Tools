using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[GenerateEquality]
public partial class InjectField
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public bool IsNullable { get; set; }
}
