using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.SourceGenerators.Diagnostics;

/// <summary>
/// What INTF001 considers a candidate for an interface, and what counts as already satisfying one.
/// </summary>
/// <remarks>
/// Shared deliberately. The analyzer and the code fix each used to carry their own copy of this
/// filter and the copies had drifted: the fix's dropped the "methods and properties only" clause,
/// so a public field reached the member factory and threw <see cref="System.NotImplementedException"/>
/// at whoever clicked the light bulb. A predicate that must agree in two places belongs in one.
/// </remarks>
internal static class InterfaceMemberCandidates
{
    /// <summary>Diagnostic property carrying the member the diagnostic was reported for.</summary>
    public const string MemberNameProperty = "TargetMember";

    /// <summary>
    /// The public instance members that could meaningfully be declared on an interface.
    /// </summary>
    /// <remarks>
    /// Methods and properties only. Fields cannot be declared on an interface at all; events and
    /// nested types could in principle but are not something this rule generates, and the analyzer
    /// has never reported them. Constructors are excluded twice over — explicitly here, and by
    /// <c>CanBeReferencedByName</c>, which is false for <c>.ctor</c>. Indexers are excluded by the
    /// same check (<c>this[]</c> is not referenceable by name), operators and constants by
    /// <c>IsStatic</c>, and a finalizer by its accessibility.
    /// </remarks>
    public static IEnumerable<ISymbol> In(INamedTypeSymbol type, INamedTypeSymbol? ignoreAttributeSymbol)
        => type.GetMembers()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public
                        && !m.IsStatic
                        && m.CanBeReferencedByName
                        && !m.IsImplicitlyDeclared
                        && (m is IMethodSymbol || m is IPropertySymbol))
            .Where(m => !(m is IMethodSymbol method
                          && (method.MethodKind == MethodKind.Constructor
                              || method.MethodKind == MethodKind.StaticConstructor)))
            .Where(m => !m.GetAttributes().Any(attr =>
                SymbolEqualityComparer.Default.Equals(attr.AttributeClass, ignoreAttributeSymbol)));

    /// <summary>Every member reachable through <paramref name="iface"/>, including its base interfaces.</summary>
    public static IEnumerable<ISymbol> MembersOf(INamedTypeSymbol iface)
        => iface.GetMembers().Concat(iface.AllInterfaces.SelectMany(i => i.GetMembers()));

    /// <summary>
    /// The names satisfied by <em>any</em> interface the type declares.
    /// </summary>
    /// <remarks>
    /// The union is the whole point. Testing membership against one interface at a time meant every
    /// public member had to appear on every implemented interface, so a class implementing two
    /// interfaces reported each member against the other one — false positives that could not be
    /// suppressed without suppressing the rule.
    ///
    /// Metadata-only interfaces are included here even though they are never offered as a fix
    /// target: they genuinely do satisfy a member, and leaving them out would report everything
    /// <see cref="System.IDisposable"/> carries.
    /// </remarks>
    public static ISet<string> SatisfiedNames(INamedTypeSymbol type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var iface in type.Interfaces)
            foreach (var member in MembersOf(iface))
                names.Add(member.Name);

        return names;
    }

    /// <summary>
    /// The interfaces a fix could actually edit: those declared by the type and written in source.
    /// </summary>
    /// <remarks>
    /// <see cref="INamedTypeSymbol.Interfaces"/>, not <c>AllInterfaces</c> — an interface inherited
    /// through a base class is that class's business, not this type's.
    /// </remarks>
    public static IReadOnlyList<INamedTypeSymbol> EditableInterfaces(INamedTypeSymbol type)
        => type.Interfaces.Where(i => i.Locations.Any(l => l.IsInSource)).ToArray();
}
