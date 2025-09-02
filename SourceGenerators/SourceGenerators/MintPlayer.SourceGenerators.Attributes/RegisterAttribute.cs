using Microsoft.Extensions.DependencyInjection;

namespace MintPlayer.SourceGenerators.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RegisterAttribute : Attribute
{
    public RegisterAttribute(ServiceLifetime lifetime, string methodNameHint = default, string factory = default) { }
    public RegisterAttribute(Type interfaceType, ServiceLifetime lifetime, string methodNameHint = default, string factory = default) { }
}
