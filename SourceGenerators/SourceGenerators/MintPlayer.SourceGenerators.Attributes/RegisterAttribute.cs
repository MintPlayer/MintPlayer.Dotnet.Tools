using Microsoft.Extensions.DependencyInjection;

namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Specifies that a class should be registered as a service in a dependency injection container.
/// </summary>
/// <remarks>
/// This attribute is used to indicate that a class should be registered with a specified service
/// lifetime and, optionally, as an implementation of a specific interface. It supports additional configuration through
/// optional parameters such as a method name hint.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RegisterAttribute : Attribute
{
    public RegisterAttribute(ServiceLifetime lifetime, string methodNameHint = default) { }
    public RegisterAttribute(Type interfaceType, ServiceLifetime lifetime, string methodNameHint = default) { }
}


/// <summary>
/// Specifies that the attributed method is a factory method to be registered for dependency injection or similar
/// purposes.
/// </summary>
/// <remarks>
/// This attribute can be applied to methods to indicate that they should be treated as factory methods.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public class RegisterFactoryAttribute : Attribute
{
    public RegisterFactoryAttribute() { }
}