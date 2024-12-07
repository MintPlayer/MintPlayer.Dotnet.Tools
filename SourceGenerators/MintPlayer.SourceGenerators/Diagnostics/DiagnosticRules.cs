using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Diagnostics;

public static class DiagnosticRules
{
    public static readonly DiagnosticDescriptor MissingInterfaceMemberRule = new DiagnosticDescriptor(
        "INTF001",
        "Interface implementation mismatch",
        "Public member '{0}' is not defined in the interface '{1}'",
        "Design",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
