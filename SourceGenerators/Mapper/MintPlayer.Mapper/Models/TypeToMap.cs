using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[AutoValueComparer]
public partial class TypeToMap
{
    public string DeclaredType { get; set; }
    public PropertyDeclaration[] DeclaredProperties { get; set; } = [];

    public string MappingType { get; set; }
    public PropertyDeclaration[] MappingProperties { get; set; } = [];
}
