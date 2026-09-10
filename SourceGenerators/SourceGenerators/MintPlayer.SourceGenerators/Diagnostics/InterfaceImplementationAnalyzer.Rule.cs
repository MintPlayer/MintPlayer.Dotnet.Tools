using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor MissingInterfaceMemberRule = new DiagnosticDescriptor(
        id: "INTF001",
        title: "Interface implementation mismatch",
        // {1} is the candidate interface list, not a single target: the diagnostic reports that a
        // member is on none of them, and choosing which one to add it to belongs to the code fix,
        // which offers an action per candidate.
        messageFormat: "Public member '{0}' is not defined in any implemented interface ({1})",
        category: string.Empty,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
