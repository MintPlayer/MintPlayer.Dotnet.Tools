using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor UnusedUsingsRule = new DiagnosticDescriptor(
        "RemoveUnusedUsings",
        "Unused using directive",
        "Using directive is unused",
        "Cleanup",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );
}