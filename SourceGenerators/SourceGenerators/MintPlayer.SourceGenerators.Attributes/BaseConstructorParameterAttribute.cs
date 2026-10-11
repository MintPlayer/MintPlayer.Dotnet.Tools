namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Not read by any generator: applying it has no effect. It will be removed in 13.0.
/// </summary>
[Obsolete("BaseConstructorParameterAttribute is not read by any generator and has no effect. It will be removed in MintPlayer.SourceGenerators 13.0.")]
[AttributeUsage(AttributeTargets.Class)]
public class BaseConstructorParameterAttribute<T> : Attribute
{
    public BaseConstructorParameterAttribute(string paramName, T value)
    {
    }
}
