namespace MintPlayer.Mapper.Attributes;

/// <summary>
/// Indicates that the decorated class should be mapped as a dictionary during serialization or data transformation
/// processes.
/// </summary>
/// <remarks>Apply this attribute to a class to signal that it represents a dictionary-like structure for mapping purposes.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MapAsDictionaryAttribute : Attribute
{
}
