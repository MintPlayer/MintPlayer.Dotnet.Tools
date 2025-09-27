namespace MintPlayer.Mapper.Attributes;

/// <summary>
/// Configures how the generated mapper will treat this property.
/// Use this attribute when the source/destination property have the same type.
/// </summary>
/// <param name="state">Indicate what state this property holds (plaintext, base64, ...)</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class MapperStateAttribute<TEnum>(TEnum state) : Attribute
    where TEnum : struct, Enum
{
}
