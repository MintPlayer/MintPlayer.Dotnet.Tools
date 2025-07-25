using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor UnusedUsingsRule = new DiagnosticDescriptor(
        id: "MP001",
        title: "Unused Usings",
        messageFormat: "The using directive for '{0}' is unused",
        category: "CodeQuality",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
