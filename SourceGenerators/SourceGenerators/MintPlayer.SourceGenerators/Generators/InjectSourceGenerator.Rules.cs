using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Generators;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor PostConstructMustBeParameterless = new(
        id: "INJECT001",
        title: "PostConstruct method must be parameterless",
        messageFormat: "Method '{0}' marked with [PostConstruct] must be parameterless",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OnlyOnePostConstructAllowed = new(
        id: "INJECT002",
        title: "Only one PostConstruct method allowed per class",
        messageFormat: "Class '{0}' has multiple methods marked with [PostConstruct]. Only one is allowed per class.",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PostConstructCannotBeStatic = new(
        id: "INJECT003",
        title: "PostConstruct method cannot be static",
        messageFormat: "Method '{0}' marked with [PostConstruct] cannot be static",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PostConstructRequiresInject = new(
        id: "INJECT004",
        title: "PostConstruct method in class without injected members",
        messageFormat: "Method '{0}' is marked with [PostConstruct] but class '{1}' has no members marked with [Inject]",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // The generated constructor calls the method and discards the result. For an async method that is
    // fire-and-forget: an exception is lost and the object is handed out before initialization finished.
    public static readonly DiagnosticDescriptor PostConstructMustReturnVoid = new(
        id: "INJECT005",
        title: "PostConstruct method must return void",
        messageFormat: "Method '{0}' marked with [PostConstruct] must return void, not '{1}'. A constructor cannot await it.",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // {1} says why: a type (the target or a containing one) that is not partial, a file-local type, or a kind
    // that has no constructor (an interface).
    public static readonly DiagnosticDescriptor InjectTypeCannotBeExtended = new(
        id: "INJECT006",
        title: "Type must be partial to receive a generated constructor",
        messageFormat: "No constructor can be generated for '{0}': {1}",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // A positional record, or a class or struct with a primary constructor: a second constructor would have
    // to chain to it (CS8862), and it cannot supply the primary constructor's arguments.
    public static readonly DiagnosticDescriptor InjectTypeHasParameterList = new(
        id: "INJECT007",
        title: "Type with a parameter list cannot receive a generated constructor",
        messageFormat: "'{0}' declares a parameter list (a primary constructor), so no constructor can be generated for its [Inject] members. Remove the parameter list, or take the dependencies as primary-constructor parameters.",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
