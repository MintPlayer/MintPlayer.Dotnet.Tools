using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Generators;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor ImplementInterfaceTypeNotPartial = new(
        id: "IMPLINTF001",
        title: "[ImplementInterface] requires a partial type",
        messageFormat: "[ImplementInterface] cannot extend '{0}': type '{1}' is not partial",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ImplementInterfaceNotAnInterface = new(
        id: "IMPLINTF002",
        title: "[ImplementInterface] argument is not an interface",
        messageFormat: "[ImplementInterface] cannot implement '{0}': it {1}",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ImplementInterfaceUnsupportedMember = new(
        id: "IMPLINTF003",
        title: "[ImplementInterface] cannot implement a member without an obvious body",
        messageFormat: "'{0}' is {1}, which [ImplementInterface] cannot implement{2}; implement it by hand in '{3}'",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ImplementInterfaceUnsupportedProperty = new(
        id: "IMPLINTF004",
        title: "[ImplementInterface] only generates read/write properties",
        messageFormat: "'{0}' is {1} property, and [ImplementInterface] only generates read/write properties{2}; implement it by hand in '{3}'",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ImplementInterfaceConflict = new(
        id: "IMPLINTF005",
        title: "[ImplementInterface] interfaces declare conflicting members",
        messageFormat: "'{0}' and '{1}' conflict and cannot both be implemented by [ImplementInterface] on '{2}'; implement one of them explicitly by hand",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ImplementInterfaceUnsupportedTarget = new(
        id: "IMPLINTF006",
        title: "[ImplementInterface] is not supported on this kind of type",
        messageFormat: "[ImplementInterface] cannot be applied to '{0}': it {1}",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ImplementInterfaceDuplicate = new(
        id: "IMPLINTF007",
        title: "[ImplementInterface] names an interface twice",
        messageFormat: "Interface '{0}' is named more than once in [ImplementInterface] on '{1}'",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ImplementInterfaceMemberMismatch = new(
        id: "IMPLINTF009",
        title: "Hand-written member does not match the interface member",
        messageFormat: "'{0}' does not implement '{2}': it is {1}. Fix it, or delete '{0}' and let [ImplementInterface] implement it.",
        category: "MintPlayer.SourceGenerators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
