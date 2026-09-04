using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor ErasedEquivalencyRule = new DiagnosticDescriptor(
        id: "MPA0004",
        title: "Equivalency expectation is erased to object",
        messageFormat: "An expectation cast to object falls back to reflection instead of the generated accessors, and cannot be configured with Excluding/Including. Pass the expectation as its concrete type.",
        category: "MintPlayer.Assertions",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
