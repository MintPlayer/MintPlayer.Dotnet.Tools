using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.ValueComparers;

namespace MintPlayer.SourceGenerators.Models;

[ValueComparer(typeof(ServiceRegistrationComparer))]
public class ServiceRegistration
{
    public string? ServiceTypeName { get; set; }
    public string? ImplementationTypeName { get; set; }
    public ServiceLifetime Lifetime { get; set; }
    public string MethodNameHint { get; set; } = string.Empty;
}
