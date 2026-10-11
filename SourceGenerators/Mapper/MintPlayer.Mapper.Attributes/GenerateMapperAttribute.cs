namespace MintPlayer.Mapper.Attributes;

/// <summary>
/// Generates mapper extension methods between two types. A type that cannot be instantiated is reported as MAPC005,
/// one the generated mapper cannot access as MAPC006.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly, Inherited = false, AllowMultiple = true)]
public class GenerateMapperAttribute : Attribute
{
    /// <summary>
    /// On a class or struct: generates extension methods that map between the decorated type and
    /// <paramref name="mapType"/>, in both directions.
    /// </summary>
    /// <param name="mapType">The type to map to and from.</param>
    /// <param name="methodName">The name of the method that maps to the decorated type, prefixed with <c>MapTo</c>
    /// when it does not start with it. Defaults to <c>MapTo</c> followed by the decorated type's name.</param>
    public GenerateMapperAttribute(Type mapType, string methodName = null)
    {
    }

    /// <summary>
    /// On the assembly: generates extension methods that map between <paramref name="sourceType"/> and
    /// <paramref name="targetType"/>, in both directions, for types you cannot decorate.
    /// </summary>
    /// <param name="sourceType">One of the two types.</param>
    /// <param name="targetType">The other type.</param>
    /// <param name="methodName">The method-name prefix, <c>MapTo</c> by default: <c>"Convert"</c> generates
    /// <c>Convert{TargetType}()</c> and <c>Convert{SourceType}()</c>.</param>
    public GenerateMapperAttribute(Type sourceType, Type targetType, string methodName = null)
    {
    }
}
