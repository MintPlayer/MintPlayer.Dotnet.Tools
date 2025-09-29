namespace MintPlayer.Mapper.Attributes;

/// <summary>
/// Indicates that this type should be treated as a dictionary when mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class MapAsDictionaryAttribute : Attribute
{
}
