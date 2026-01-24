using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

/// <summary>
/// Holds assembly-level configuration for service registration generation.
/// </summary>
[AutoValueComparer]
public partial class AssemblyRegistrationConfig
{
    /// <summary>
    /// The name of the assembly being processed.
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    /// The default method name configured via ServiceRegistrationConfigurationAttribute.
    /// Null if not specified.
    /// </summary>
    public string? DefaultMethodName { get; set; }

    /// <summary>
    /// The default accessibility configured via ServiceRegistrationConfigurationAttribute.
    /// </summary>
    public EGeneratedAccessibility DefaultAccessibility { get; set; } = EGeneratedAccessibility.Unspecified;
}
