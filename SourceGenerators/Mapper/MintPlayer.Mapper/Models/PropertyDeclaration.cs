using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[AutoValueComparer]
public partial class PropertyDeclaration
{
    public string PropertyName { get; set; }
    public string PropertyType { get; set; }
    public string PropertyTypeName { get; set; }
    public string? Alias { get; set; }
    public bool IsStatic { get; set; }
    public bool IsPrimitive { get; set; }

    public override string ToString() => $"{PropertyName} ({PropertyType})";
}
