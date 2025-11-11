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
}
