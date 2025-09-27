namespace MintPlayer.Mapper.Attributes;

/// <summary>
/// Configures how the generated mapper will treat this property.
/// </summary>
/// <param name="alias">The generated mapper will map properties with the same alias.</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapperAliasAttribute(string alias) : Attribute
{
}
