using Microsoft.Extensions.DependencyInjection;

namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Specifies the accessibility of the generated service registration extension method.
/// </summary>
public enum EGeneratedAccessibility
{
    /// <summary>Indicates that the accessibility of the generated extension method is unspecified</summary>
    Unspecified,
    /// <summary>Indicates that the generated extension method should be public</summary>
    Public,
    /// <summary>Indicates that the generated extension method should be internal</summary>
    Internal,
}

/// <summary>
/// Specifies that a class should be registered as a service in a dependency injection container.
/// </summary>
/// <remarks>
/// This attribute is used to indicate that a class should be registered with a specified service
/// lifetime and, optionally, as an implementation of a specific interface. It supports additional configuration through
/// optional parameters such as a method name hint and accessibility of the generated extension method.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public class RegisterAttribute : Attribute
{
    /// <summary>
    /// Registers a class as its own service type (no interface).
    /// Use on class declarations.
    /// </summary>
    public RegisterAttribute(ServiceLifetime lifetime, string methodNameHint = default, EGeneratedAccessibility accessibility = EGeneratedAccessibility.Unspecified) { }

    /// <summary>
    /// Registers a class as an implementation of an interface.
    /// Use on class declarations.
    /// </summary>
    public RegisterAttribute(Type interfaceType, ServiceLifetime lifetime, string methodNameHint = default, EGeneratedAccessibility accessibility = EGeneratedAccessibility.Unspecified) { }

    /// <summary>
    /// Registers a third-party or external class as an implementation of a service type.
    /// Use at assembly level for types you don't own.
    /// The service type can be an interface or the implementation type itself.
    /// </summary>
    /// <example>
    /// [assembly: Register(typeof(IGitHubClient), typeof(GitHubClient), ServiceLifetime.Scoped)]
    /// [assembly: Register(typeof(ThirdPartyClass), typeof(ThirdPartyClass), ServiceLifetime.Singleton)]
    /// </example>
    public RegisterAttribute(Type serviceType, Type implementationType, ServiceLifetime lifetime, string methodNameHint = default, EGeneratedAccessibility accessibility = EGeneratedAccessibility.Unspecified) { }
}

/// <summary>
/// Specifies that the attributed method is a factory method to be registered for dependency injection or similar purposes.
/// </summary>
/// <remarks>
/// This attribute can be applied to methods to indicate that they should be treated as factory methods.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public class RegisterFactoryAttribute : Attribute
{
    public RegisterFactoryAttribute() { }
}