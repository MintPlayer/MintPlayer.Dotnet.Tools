namespace MintPlayer.Mapper.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
public class GenerateMapperAttribute : Attribute
{
    public GenerateMapperAttribute(Type mapType)
    {
    }
}
