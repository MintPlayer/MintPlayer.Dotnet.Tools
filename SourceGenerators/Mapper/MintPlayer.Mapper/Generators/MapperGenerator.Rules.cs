using Microsoft.CodeAnalysis;

namespace MintPlayer.Mapper.Generators;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor ConversionMethodMissingStateRule = new(
        id: "MAPC001",
        title: "Conversion method missing state parameters",
        messageFormat: "The conversion method '{0}' is missing state parameters 'inState' and 'outState'",
        category: string.Empty,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConversionMethodUnnecessaryStateRule = new(
        id: "MAPC002",
        title: "Conversion method should not have state parameters",
        messageFormat: "The conversion method '{0}' should not have state parameters 'inState' and 'outState'",
        category: string.Empty,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}