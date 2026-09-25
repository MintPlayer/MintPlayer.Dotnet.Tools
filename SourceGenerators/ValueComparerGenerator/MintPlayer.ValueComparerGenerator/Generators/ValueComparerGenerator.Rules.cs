using Microsoft.CodeAnalysis;

namespace MintPlayer.ValueComparerGenerator.Generators;

/// <summary>The diagnostics <see cref="ValueComparerGenerator"/> reports. Models refer to them by <see cref="DiagnosticDescriptor.Id"/> plus a variant key.</summary>
public static class ValueComparerDiagnostics
{
    private const string Category = "ValueComparerGenerator";

    public static readonly DiagnosticDescriptor UserDeclaredMember = new(
        id: "MINT002",
        title: "Equality member already declared",
        messageFormat: "'{0}' already declares {1}; [AutoValueComparer] did not generate it{2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: """
            A member that the type declares itself is never generated. IEquatable<T> is still added when the type declares Equals(T).
            When the type declares only one of Equals(object) and GetHashCode(), the diagnostic is raised as a warning instead:
            the generated other half compares the generated property list, which may not be what the declared one compares.
            """);

    /// <summary>
    /// The warning variant of <see cref="UserDeclaredMember"/>. Same descriptor, raised with a Warning effective
    /// severity: a second descriptor with the same id and another severity trips analyzer release tracking (RS2001).
    /// </summary>
    public const string UserDeclaredHalfOfPair = nameof(UserDeclaredHalfOfPair);

    public static readonly DiagnosticDescriptor DerivedTypeNotPartial = new(
        id: "MINT003",
        title: "Type deriving from an [AutoValueComparer] type must be partial",
        messageFormat: "'{0}' derives from [AutoValueComparer] type '{1}' and must be partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Without its own generated members, a derived type inherits the base's EqualsCore and compares only the base's properties: two instances that differ in a derived property compare equal.");

    public static readonly DiagnosticDescriptor ModelNotPartial = new(
        id: "MINT003",
        title: "[AutoValueComparer] type must be partial",
        messageFormat: "'{0}' is marked [AutoValueComparer] and must be partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The equality members are generated into another part of the type, so the type must be partial.");

    public static readonly DiagnosticDescriptor InvalidEqualityComparer = new(
        id: "MINT004",
        title: "Invalid [UseEqualityComparer] type",
        messageFormat: "'{0}' cannot compare property '{1}' of type '{2}': {3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The comparer type must implement IEqualityComparer<T> for the property's type, and have a static Instance member or a public parameterless constructor.");

    public static readonly DiagnosticDescriptor ReferenceEqualityOnly = new(
        id: "MINT005",
        title: "Property type has only reference equality",
        messageFormat: "Property '{0}' of '{1}' compares '{2}' by reference, so equal values never compare equal. Make '{2}' equatable, mark it [AutoValueComparer], or put [UseEqualityComparer] on the property.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A class that neither overrides Equals nor implements IEquatable<T> compares by reference. In an incremental-generator model, that makes every step holding it report Modified, so nothing downstream caches.");

    public static readonly DiagnosticDescriptor ContainingTypeNotPartial = new(
        id: "MINT006",
        title: "Containing type must be partial",
        messageFormat: "'{0}' is nested in '{1}', which must be partial for [AutoValueComparer] to generate the equality members of '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated file reopens every containing type, so each of them must be partial.");

    private static readonly Dictionary<string, DiagnosticDescriptor> byKey = new(StringComparer.Ordinal)
    {
        [nameof(UserDeclaredMember)] = UserDeclaredMember,
        [nameof(DerivedTypeNotPartial)] = DerivedTypeNotPartial,
        [nameof(ModelNotPartial)] = ModelNotPartial,
        [nameof(InvalidEqualityComparer)] = InvalidEqualityComparer,
        [nameof(ReferenceEqualityOnly)] = ReferenceEqualityOnly,
        [nameof(ContainingTypeNotPartial)] = ContainingTypeNotPartial,
    };

    public static Diagnostic Create(string key, Location? location, object[] messageArgs)
        => key == UserDeclaredHalfOfPair
            ? Diagnostic.Create(UserDeclaredMember, location, DiagnosticSeverity.Warning, additionalLocations: null, properties: null, messageArgs)
            : Diagnostic.Create(byKey[key], location, messageArgs);
}
