using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.SourceGenerator.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor UnsupportedGenerateAssertionRule = new DiagnosticDescriptor(
        id: "MPAG001",
        title: "Unsupported [GenerateAssertion] method",
        messageFormat: "No assertion is generated for '{0}': {1}",
        category: "MintPlayer.Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[GenerateAssertion] applies to a static method that returns bool and takes the subject as its first parameter.");
}
