using System;

namespace MintPlayer.SourceGenerators.Attributes
{
    public class BaseConstructorParameterAttribute : Attribute
    {
        public BaseConstructorParameterAttribute(Type parameterType, string paramName, object value)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BaseConstructorParameterAttribute<T> : BaseConstructorParameterAttribute
    {
        public BaseConstructorParameterAttribute(string paramName, T value) : base(typeof(T), paramName, value)
        {
        }
    }
}
