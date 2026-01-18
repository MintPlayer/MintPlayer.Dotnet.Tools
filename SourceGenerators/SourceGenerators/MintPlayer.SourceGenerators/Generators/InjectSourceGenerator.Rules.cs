using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Generators;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor PostConstructMustBeParameterless = new(
        id: "INJECT001",
        title: "PostConstruct method must be parameterless",
        messageFormat: "Method '{0}' marked with [PostConstruct] must be parameterless",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OnlyOnePostConstructAllowed = new(
        id: "INJECT002",
        title: "Only one PostConstruct method allowed per class",
        messageFormat: "Class '{0}' has multiple methods marked with [PostConstruct]. Only one is allowed per class.",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PostConstructCannotBeStatic = new(
        id: "INJECT003",
        title: "PostConstruct method cannot be static",
        messageFormat: "Method '{0}' marked with [PostConstruct] cannot be static",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PostConstructRequiresInject = new(
        id: "INJECT004",
        title: "PostConstruct method in class without injected members",
        messageFormat: "Method '{0}' is marked with [PostConstruct] but class '{1}' has no members marked with [Inject]",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
