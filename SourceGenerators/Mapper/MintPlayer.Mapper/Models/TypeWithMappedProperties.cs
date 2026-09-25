using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.Mapper.Models;

[GenerateEquality]
public partial class TypeWithMappedProperties
{
    public TypeToMap TypeToMap { get; set; } = null!;
    public (PropertyDeclaration Source, PropertyDeclaration Destination)[] MappedProperties { get; set; } = [];
}