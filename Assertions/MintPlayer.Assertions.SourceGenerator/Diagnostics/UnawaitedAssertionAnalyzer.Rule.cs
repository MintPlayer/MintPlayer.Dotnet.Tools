using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor UnawaitedAssertionRule = new DiagnosticDescriptor(
        id: "MPA0001",
        title: "Assertion is not awaited",
        messageFormat: "This assertion returns an awaitable and must be awaited. Otherwise the test can pass even when the assertion fails.",
        category: "MintPlayer.Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
