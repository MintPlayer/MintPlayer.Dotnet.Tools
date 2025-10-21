using Microsoft.CodeAnalysis;

namespace MintPlayer.Mapper.Generators;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor GenerateMapperTwoParameters = new(
        id: "MAPC001",
        title: "When applied to assembly, [GenerateMapper] must have 2 types as parameters",
        messageFormat: "When applied to assembly, [GenerateMapper] must have 2 types as parameters",
        category: "MapperGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenerateMapperOneParameter = new(
        id: "MAPC002",
        title: "When applied to type, [GenerateMapper] must have 1 type as parameter",
        messageFormat: "When applied to type, [GenerateMapper] must have 1 type as parameter",
        category: "MapperGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InitOnlyPropertyNotMapped = new(
        id: "MAPC003",
        title: "Init-only property not mapped",
        messageFormat: "The property '{0}' on type '{1}' is init-only and will not be mapped from '{2}'",
        category: "Mapping",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}