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

    public static readonly DiagnosticDescriptor OnlyOneConsoleAppAllowed = new(
        id: "COMLAPP002",
        title: "Only one console app with the [ConsoleApp] attribute is allowed",
        messageFormat: "Only one console app with the [ConsoleApp] attribute is allowed",
        category: "ConsoleAppGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}