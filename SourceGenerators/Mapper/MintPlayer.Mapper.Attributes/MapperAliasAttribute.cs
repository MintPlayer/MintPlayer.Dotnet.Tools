namespace MintPlayer.Mapper.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MapperAliasAttribute : Attribute
{
    /// <summary>
    /// Configures how the generated mapper will treat this property.
    /// Use this overload when the source/destination property have different types.
    /// </summary>
    /// <param name="alias">The generated mapper will map properties with the same alias.</param>
    public MapperAliasAttribute(string alias) { }

    /// <summary>
    /// Configures how the generated mapper will treat this property.
    /// Use this overload when the source/destination property have the same type.
    /// </summary>
    /// <param name="alias">The generated mapper will map properties with the same alias.</param>
    /// <param name="state">Indicate what state this property holds (plaintext, base64, ...)</param>
    public MapperAliasAttribute(string alias, string state) { }
}
