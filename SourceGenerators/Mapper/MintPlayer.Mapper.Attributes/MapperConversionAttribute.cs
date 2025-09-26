namespace MintPlayer.Mapper.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class MapperConversionAttribute : Attribute
{
    public MapperConversionAttribute() { }
    public MapperConversionAttribute(string inState, string outState) { }
}
