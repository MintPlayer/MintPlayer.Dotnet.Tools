using Microsoft.Extensions.DependencyInjection;

namespace MintPlayer.SourceGenerators.Attributes;

// For now don't allow multiple registrations of the same service
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class RegisterAttribute : Attribute
{
    public RegisterAttribute(ServiceLifetime lifetime, string methodNameHint = "") { }
    public RegisterAttribute(Type interfaceType, ServiceLifetime lifetime, string methodNameHint = "") { }
}
