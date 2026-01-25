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
}
