#nullable enable

namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Configures the default service registration method name at the assembly level.
/// </summary>
/// <remarks>
/// Use this attribute to override the default method name for generated service registration extension methods.
/// When not specified, the method name defaults to "Add{AssemblyName}".
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public class ServiceRegistrationConfigurationAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the default method name for the generated service registration extension method.
    /// If not specified, the method name defaults to "Add{AssemblyName}".
    /// The "Add" prefix will be automatically added if not present.
    /// </summary>
    public string? DefaultMethodName { get; set; }

    /// <summary>
    /// Gets or sets the default accessibility of the generated extension method.
    /// </summary>
    public EGeneratedAccessibility DefaultAccessibility { get; set; } = EGeneratedAccessibility.Unspecified;
}
