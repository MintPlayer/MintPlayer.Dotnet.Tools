using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Generators;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor GenericMethodTypeNotExtendable = new(
        id: "GENMETHOD001",
        title: "[GenericMethod] requires a partial containing type",
        messageFormat: "No overloads are generated for '{0}': type '{1}' {2}",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericMethodMustBePrivate = new(
        id: "GENMETHOD002",
        title: "[GenericMethod] method must be private",
        messageFormat: "Method '{0}' marked with [GenericMethod] must be private, not {1}: the generated public overloads would compete with it",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericMethodInvalidShape = new(
        id: "GENMETHOD003",
        title: "[GenericMethod] method has an unsupported shape",
        messageFormat: "Method '{0}' marked with [GenericMethod] {1}. It must take exactly one parameter that a collection expression can build from object elements (e.g. object[], IEnumerable<object>, List<object>, ReadOnlySpan<object>).",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericMethodCountOutOfRange = new(
        id: "GENMETHOD004",
        title: "[GenericMethod] Count out of range",
        messageFormat: "[GenericMethod] Count on '{0}' is {1}; it must be between 2 and 16",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
