using Microsoft.CodeAnalysis;

namespace MintPlayer.CommandLineApp.Generators;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor CannotHaveTopLevelStatements = new(
        id: "COMLAPP001",
        title: "A console app that uses the [ConsoleApp] attribute cannot have top-level statements",
        messageFormat: "A console app that uses the [ConsoleApp] attribute cannot have top-level statements",
        category: "ConsoleAppGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}