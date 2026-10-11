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

    /// <summary>The generated mapper creates both types with <c>new</c>, which an interface, an abstract or static class, a ref struct or an open generic type does not allow.</summary>
    public static readonly DiagnosticDescriptor MappedTypeCannotBeInstantiated = new(
        id: "MAPC005",
        title: "[GenerateMapper] type cannot be instantiated",
        messageFormat: "No mapper is generated for '{0}', because {1}. A mapped type must be a non-abstract class or struct.",
        category: "MapperGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>The generated <c>MapperExtensions</c> class is a top-level class, so it cannot name a private, protected or file-local type.</summary>
    public static readonly DiagnosticDescriptor MappedTypeNotAccessible = new(
        id: "MAPC006",
        title: "[GenerateMapper] type is not accessible from the generated mapper",
        messageFormat: "No mapper is generated for '{0}', because the generated MapperExtensions class cannot access it. A mapped type, and every type it is nested in, must be internal or public.",
        category: "MapperGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>The generated <c>ConvertProperty</c> calls the method by its class name, without type arguments.</summary>
    public static readonly DiagnosticDescriptor ConversionMethodCannotBeCalled = new(
        id: "MAPC007",
        title: "[MapperConversion] method cannot be called from the generated mapper",
        messageFormat: "The [MapperConversion] method '{0}' is not used, because {1}",
        category: "MapperGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
