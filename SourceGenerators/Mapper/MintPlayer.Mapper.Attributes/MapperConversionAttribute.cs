namespace MintPlayer.Mapper.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class MapperConversionAttribute : Attribute
{
    public MapperConversionAttribute() { }
}

// TODO: AllowMultiple => conversions between the enum values
// The method must have 2 parameters of the enum type, one for input state, one for output state
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class MapperConversionAttribute<TEnum> : Attribute
    where TEnum : struct, Enum
{
    public MapperConversionAttribute(TEnum inState, TEnum outState) { }
}
