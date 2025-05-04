using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor MissingInterfaceMemberRule = new DiagnosticDescriptor(
        id: "INTF001",
        title: "Interface implementation mismatch",
        messageFormat: "Public member '{0}' is not defined in the interface '{1}'",
        category: string.Empty,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
