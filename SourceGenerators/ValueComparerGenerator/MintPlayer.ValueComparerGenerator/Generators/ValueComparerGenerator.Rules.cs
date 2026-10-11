using Microsoft.CodeAnalysis;

namespace MintPlayer.ValueComparerGenerator.Generators;

/// <summary>The diagnostics <see cref="ValueComparerGenerator"/> reports, held by discovery as Tools' <c>DiagnosticInfo</c>.</summary>
public static class ValueComparerDiagnostics
{
    private const string Category = "ValueComparerGenerator";

    public static readonly DiagnosticDescriptor UserDeclaredMember = new(
        id: "MINT002",
        title: "Equality member already declared",
        messageFormat: "'{0}' already declares {1}; [GenerateEquality] did not generate it{2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: """
            A member that the type declares itself is never generated. IEquatable<T> is still added when the type declares Equals(T).
            When the type declares only one of Equals(object) and GetHashCode(), MINT008 (a warning) is reported for it instead.
            """);

    /// <summary>
    /// The type declares only one of <c>Equals(object)</c> and <c>GetHashCode()</c>, so the generated other half can
    /// disagree with it. Its own id, not a warning-severity MINT002: Tools' <c>DiagnosticInfo</c> resolves a rule by
    /// id, so one id is one descriptor with one severity.
    /// </summary>
    public static readonly DiagnosticDescriptor UserDeclaredHalfOfPair = new(
        id: "MINT008",
        title: "Only one of Equals(object) and GetHashCode() is declared",
        messageFormat: "'{0}' already declares {1}; [GenerateEquality] did not generate it{2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The type declares only one of Equals(object) and GetHashCode(): the generated other half compares the generated property list, which may not be what the declared one compares.");

    /// <summary>
    /// MINT003, for an attributed type and for a type deriving from one. One descriptor per id (see
    /// <see cref="UserDeclaredHalfOfPair"/>), so the two messages differ only in their arguments: use
    /// <see cref="ModelNotPartialReason"/> or <see cref="DerivedTypeNotPartialReason"/>.
    /// </summary>
    public static readonly DiagnosticDescriptor NotPartial = new(
        id: "MINT003",
        title: "[GenerateEquality] type must be partial",
        messageFormat: "'{0}' {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The equality members are generated into another part of the type, so the type must be partial. Without its own generated members, a type deriving from a [GenerateEquality] type inherits the base's EqualsCore and compares only the base's properties: two instances that differ in a derived property compare equal.");

    public const string ModelNotPartialReason = "is marked [GenerateEquality] and must be partial";

    public static string DerivedTypeNotPartialReason(string root) => $"derives from [GenerateEquality] type '{root}' and must be partial";

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
        messageFormat: "Property '{0}' of '{1}' compares '{2}' by reference, so equal values never compare equal. Make '{2}' equatable, mark it [GenerateEquality], or put [UseEqualityComparer] on the property.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A class that neither overrides Equals nor implements IEquatable<T> compares by reference. In an incremental-generator model, that makes every step holding it report Modified, so nothing downstream caches.");

    public static readonly DiagnosticDescriptor ContainingTypeNotPartial = new(
        id: "MINT006",
        title: "Containing type must be partial",
        messageFormat: "'{0}' is nested in '{1}', which must be partial for [GenerateEquality] to generate the equality members of '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated file reopens every containing type, so each of them must be partial.");

    public static readonly DiagnosticDescriptor UnsupportedModel = new(
        id: "MINT007",
        title: "[GenerateEquality] cannot generate equality for this type",
        messageFormat: "[GenerateEquality] cannot generate the equality members of '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: """
            Generated equality needs a type that can be re-declared as a partial in another file and boxed into Equals(object).
            A ref struct cannot be boxed; a file-local type (or a type nested in one) re-declared in the generated file would be a different type;
            a union and a static class have no value to compare.
            """);
}
