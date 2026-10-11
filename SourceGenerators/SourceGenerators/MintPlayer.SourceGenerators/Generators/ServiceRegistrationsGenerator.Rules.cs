using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Generators;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor RegisterAttributeAssemblyRequiresType = new(
        id: "REGISTER001",
        title: "When applied to assembly, [Register] must specify at least the implementation type",
        messageFormat: "When applied to assembly, [Register] must specify at least the implementation type. Use [assembly: Register(typeof(Implementation), ServiceLifetime.Scoped)] or [assembly: Register(typeof(IService), typeof(Implementation), ServiceLifetime.Scoped)]",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RegisterAttributeClassRequiresLifetime = new(
        id: "REGISTER002",
        title: "When applied to class, [Register] must not have implementation type",
        messageFormat: "When applied to class, [Register] should not specify implementation type. Use [Register(typeof(IInterface), ServiceLifetime.Scoped)] instead",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RegisterTypeNotAccessible = new(
        id: "REGISTER003",
        title: "[Register] type is not accessible from the generated registration method",
        messageFormat: "'{0}' is {1}, so the generated registration method cannot reference it. Make it internal or public, or remove [Register].",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RegisterLifetimeUndefined = new(
        id: "REGISTER004",
        title: "[Register] lifetime is not a defined ServiceLifetime",
        messageFormat: "{0} is not a defined ServiceLifetime. Use ServiceLifetime.Singleton, Scoped or Transient.",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RegisterServiceTypeNotExpressible = new(
        id: "REGISTER005",
        title: "[Register] service type cannot be expressed in the generated registration",
        messageFormat: "'{0}' cannot be registered as '{1}': {2}",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
