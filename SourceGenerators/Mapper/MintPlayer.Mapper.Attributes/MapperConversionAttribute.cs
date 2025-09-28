namespace MintPlayer.Mapper.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class MapperConversionAttribute : Attribute
{
    public MapperConversionAttribute() { }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public class MapperConversionAttribute<TEnum> : Attribute
    where TEnum : struct, Enum
{
    public MapperConversionAttribute(TEnum inState, TEnum outState) { }
}
