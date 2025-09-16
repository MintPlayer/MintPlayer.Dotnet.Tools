namespace MintPlayer.ValueComparerGenerator.Attributes;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class GenerateJoinMethodsAttribute : Attribute
{
    public GenerateJoinMethodsAttribute(uint numberOfParameters)
    {
    }
}
