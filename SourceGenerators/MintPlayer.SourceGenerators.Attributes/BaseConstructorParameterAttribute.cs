namespace MintPlayer.SourceGenerators.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class BaseConstructorParameterAttribute<T> : Attribute
{
    public BaseConstructorParameterAttribute(string paramName, T value)
    {
    }
}
