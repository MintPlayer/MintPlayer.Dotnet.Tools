using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor FluentAssertionsMigrationRule = new DiagnosticDescriptor(
        id: "MPA0100",
        title: "FluentAssertions migration available",
        messageFormat: "This file uses FluentAssertions; MintPlayer.Assertions is a drop-in for most call shapes.",
        category: "MintPlayer.Assertions",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
