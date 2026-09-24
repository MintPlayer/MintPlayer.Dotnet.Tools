using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[AutoValueComparer]
public partial class TypeWithMappedProperties
{
    public TypeToMap TypeToMap { get; set; } = null!;
    public (PropertyDeclaration Source, PropertyDeclaration Destination)[] MappedProperties { get; set; } = [];
}