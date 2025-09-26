namespace MintPlayer.Mapper.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class MapperStateAttribute : Attribute
{
    public MapperStateAttribute(string state) { }
}
