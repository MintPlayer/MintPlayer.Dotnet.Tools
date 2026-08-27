using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor AssertionScopeNotDisposedRule = new DiagnosticDescriptor(
        id: "MPA0003",
        title: "AssertionScope not disposed",
        messageFormat: "An AssertionScope that is never disposed will swallow its collected failures. Wrap it in a using.",
        category: "MintPlayer.Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
