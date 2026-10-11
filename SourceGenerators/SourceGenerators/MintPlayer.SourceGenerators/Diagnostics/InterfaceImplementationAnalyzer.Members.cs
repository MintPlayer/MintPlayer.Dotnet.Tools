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
    /// Diagnostic property carrying the member's <see cref="SignatureKey"/>, so that the fix finds the overload the
    /// diagnostic is about rather than the first member of that name.
    /// </summary>
    public const string MemberSignatureProperty = "TargetMemberSignature";

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
    /// <para>
    /// Overrides (including <c>sealed override</c>) are excluded: they add no public surface, they
    /// re-implement a member whose contract the base class already declares. <c>new</c> members are
    /// not overrides and stay candidates — hiding a member is genuinely new surface.
    /// </para>
    /// </remarks>
    public static IEnumerable<ISymbol> In(INamedTypeSymbol type, INamedTypeSymbol? ignoreAttributeSymbol)
        => type.GetMembers()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public
                        && !m.IsStatic
                        && m.CanBeReferencedByName
                        && !m.IsImplicitlyDeclared
                        && !m.IsOverride
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
    /// Whether <paramref name="member"/> is already satisfied by <em>any</em> interface the type declares.
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
    ///
    /// By signature, not by name (W16): with <c>DoIt()</c> on the interface, an added overload
    /// <c>DoIt(int)</c> is new surface and must be reported. A member is satisfied when it implements
    /// an interface member, or has the same signature as one (it may be hidden by an explicit
    /// implementation, and declaring it again would be a duplicate).
    /// </remarks>
    public static bool IsSatisfied(INamedTypeSymbol type, ISymbol member)
    {
        var signature = SignatureKey(member);
        foreach (var iface in type.Interfaces)
        {
            foreach (var interfaceMember in MembersOf(iface))
            {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(interfaceMember), member)
                    || SignatureKey(interfaceMember) == signature)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What identifies a method or property among its overloads: kind, name, generic arity and parameter types with
    /// their <c>ref</c>/<c>out</c>/<c>in</c>. A method type parameter is its position (<c>!!0</c>), so <c>M&lt;T&gt;(T)</c>
    /// on a class and <c>M&lt;U&gt;(U)</c> on an interface match. The return type is not part of it, as in C#.
    /// </summary>
    public static string SignatureKey(ISymbol member)
    {
        switch (member)
        {
            case IMethodSymbol method:
                var builder = new System.Text.StringBuilder("M:").Append(method.Name).Append('`').Append(method.TypeParameters.Length).Append('(');
                for (var i = 0; i < method.Parameters.Length; i++)
                {
                    if (i > 0) builder.Append(',');
                    var parameter = method.Parameters[i];
                    if (parameter.RefKind != RefKind.None) builder.Append(parameter.RefKind).Append(' ');
                    AppendType(builder, parameter.Type);
                }
                return builder.Append(')').ToString();
            case IPropertySymbol property:
                return $"P:{property.Name}";
            default:
                return $"{member.Kind}:{member.Name}";
        }

        static void AppendType(System.Text.StringBuilder builder, ITypeSymbol type)
        {
            foreach (var part in type.ToDisplayParts(SymbolDisplayFormat.FullyQualifiedFormat))
            {
                if (part.Symbol is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } typeParameter)
                    builder.Append("!!").Append(typeParameter.Ordinal);
                else
                    builder.Append(part.ToString());
            }
        }
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
