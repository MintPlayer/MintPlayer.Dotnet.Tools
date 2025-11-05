using System.Collections.Generic;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.CliGenerator.Models;

[AutoValueComparer]
internal sealed partial class CliOptionDefinition
{
    public string PropertyName { get; set; } = null!;
    public string PropertyType { get; set; } = null!;
    public IReadOnlyList<string> Aliases { get; set; } = System.Array.Empty<string>();
    public string? Description { get; set; }
    public bool Required { get; set; }
    public bool Hidden { get; set; }
    public string? DefaultValueExpression { get; set; }
    public bool HasDefaultValue { get; set; }
}
