using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MintPlayer.CliGenerator.Diagnostics;

public sealed partial class CliCommandInterfaceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MINTCLI001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "CLI commands must implement ICliCommand",
        messageFormat: "Class '{0}' annotated with '{1}' must implement MintPlayer.CliGenerator.Attributes.ICliCommand.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes annotated with CliCommand or CliRootCommand must implement the ICliCommand contract so the generator can discover their execution entry point."
    );
}
