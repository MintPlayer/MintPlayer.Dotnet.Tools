using Microsoft.Extensions.DependencyInjection;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace MintPlayer.SourceGenerators.Models;

[AutoValueComparer]
public partial class ServiceRegistration
{
    public string? ServiceTypeName { get; set; }
    public string? ImplementationTypeName { get; set; }
    public ServiceLifetime Lifetime { get; set; }
    public string? MethodNameHint { get; set; } = string.Empty;
    public string[] FactoryNames { get; set; } = [];
    public Attributes.EGeneratedAccessibility Accessibility { get; set; }

    /// <summary>
    /// Indicates whether this registration is for an open generic type.
    /// </summary>
    public bool IsGeneric { get; set; }

    /// <summary>
    /// Contains type parameter and constraint information for generic registrations.
    /// Null when IsGeneric is false.
    /// </summary>
    public GenericTypeInfo? GenericInfo { get; set; }
}
