using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.CliGenerator.Models;

[AutoValueComparer]
internal sealed partial class CliArgumentDefinition
{
    public string PropertyName { get; set; } = null!;
    public string PropertyType { get; set; } = null!;
    public int Position { get; set; }
    public string ArgumentName { get; set; } = null!;
    public string? Description { get; set; }
    public bool Required { get; set; }
}
