namespace MintPlayer.Mapper.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly, Inherited = false, AllowMultiple = true)]
public class GenerateMapperAttribute : Attribute
{
    // Only allow on classes and structs
    public GenerateMapperAttribute(Type mapType, string methodName = null)
    {
    }

    // Only allow on assembly
    public GenerateMapperAttribute(Type sourceType, Type targetType, string methodName = null)
    {
    }
}
