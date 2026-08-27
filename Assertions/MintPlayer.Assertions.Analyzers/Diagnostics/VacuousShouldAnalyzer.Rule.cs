using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor VacuousShouldRule = new DiagnosticDescriptor(
        id: "MPA0002",
        title: "Vacuous Should()",
        messageFormat: "Should() without an assertion does nothing; call an assertion method on the result.",
        category: "MintPlayer.Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
