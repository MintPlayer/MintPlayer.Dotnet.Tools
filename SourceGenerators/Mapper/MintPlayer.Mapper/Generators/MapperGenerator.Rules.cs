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
}