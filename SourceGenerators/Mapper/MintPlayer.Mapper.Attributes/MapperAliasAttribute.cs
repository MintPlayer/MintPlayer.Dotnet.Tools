namespace MintPlayer.Mapper.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MapperAliasAttribute : Attribute
{
    public MapperAliasAttribute(string alias)
    {
    }
}
