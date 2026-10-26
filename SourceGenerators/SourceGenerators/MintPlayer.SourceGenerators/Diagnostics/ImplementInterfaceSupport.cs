using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MintPlayer.SourceGenerators.Diagnostics;

/// <summary>
/// What the analyzers and code fixes need to know about <c>[ImplementInterface]</c>: which interfaces a type asks for,
/// and whether a hand-written property is one the generator would produce identically.
/// </summary>
internal static class ImplementInterfaceSupport
{
    private const string AttributeNamespace = "MintPlayer.SourceGenerators.Attributes";
    private const string AttributeName = "ImplementInterfaceAttribute";

    public static bool IsImplementInterfaceAttribute(INamedTypeSymbol? attributeClass)
        => attributeClass is { Name: AttributeName } && attributeClass.ContainingNamespace.ToDisplayString() == AttributeNamespace;

    /// <summary>The interfaces named in the type's <c>[ImplementInterface]</c> attributes, each with its <c>Explicit</c> flag.</summary>
    public static IReadOnlyList<(INamedTypeSymbol Interface, bool Explicit)> RequestedInterfaces(INamedTypeSymbol type)
    {
        var result = new List<(INamedTypeSymbol, bool)>();
        foreach (var attribute in type.GetAttributes().Where(a => IsImplementInterfaceAttribute(a.AttributeClass)))
        {
            var isExplicit = attribute.NamedArguments.Any(a => a.Key == "Explicit" && a.Value.Value is true);
            IEnumerable<TypedConstant> arguments = attribute.AttributeClass!.IsGenericType
                ? []
                : attribute.ConstructorArguments.Length == 1
                    ? attribute.ConstructorArguments[0].Kind == TypedConstantKind.Array ? attribute.ConstructorArguments[0].Values : [attribute.ConstructorArguments[0]]
                    : [];
            var interfaces = attribute.AttributeClass.IsGenericType
                ? attribute.AttributeClass.TypeArguments.OfType<INamedTypeSymbol>()
                : arguments.Select(a => a.Value).OfType<INamedTypeSymbol>();
            foreach (var @interface in interfaces.Where(i => i.TypeKind == TypeKind.Interface && !i.IsUnboundGenericType))
                result.Add((@interface, isExplicit));
        }
        return result;
    }

    /// <summary>
    /// The one read/write instance property, of the interfaces the type asks to implement publicly, that
    /// <paramref name="property"/> is a redundant copy of; or null when it is not redundant.
    /// </summary>
    /// <remarks>
    /// Strict on purpose: deleting the property must change nothing. Only a bare <c>public T P { get; set; }</c> (or
    /// <c>init</c>) counts: no attributes, no initializer, no bodies, no accessor modifiers, no other modifier, one
    /// declaration, and exactly the interface's type and nullability. A property that also satisfies an interface
    /// implemented explicitly, or whose name more than one requested interface declares, is never redundant.
    /// </remarks>
    public static IPropertySymbol? RedundantWith(IPropertySymbol property, CancellationToken cancellationToken)
    {
        if (property.IsStatic || property.IsIndexer || property.DeclaringSyntaxReferences.Length != 1) return null;
        if (property.ContainingType is not { } type) return null;
        if (property.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not PropertyDeclarationSyntax syntax) return null;
        if (!IsBareAutoProperty(syntax)) return null;

        var requested = RequestedInterfaces(type);
        if (requested.Count == 0) return null;

        var candidates = requested
            .SelectMany(r => new[] { r.Interface }.Concat(r.Interface.AllInterfaces).Select(i => (Interface: i, r.Explicit)))
            .SelectMany(r => r.Interface.GetMembers(property.Name).OfType<IPropertySymbol>().Select(p => (Property: p, r.Explicit)))
            .Where(c => c.Property.IsAbstract && !c.Property.IsStatic)
            .GroupBy(c => c.Property, SymbolEqualityComparer.Default)
            .ToArray();
        if (candidates.Length != 1 || candidates[0].Any(c => c.Explicit)) return null;

        var target = (IPropertySymbol)candidates[0].Key!;
        if (target.GetMethod is null || target.SetMethod is null || target.RefKind != RefKind.None) return null;
        if (!SymbolEqualityComparer.IncludeNullability.Equals(target.Type, property.Type)) return null;
        if (target.SetMethod.IsInitOnly != (property.SetMethod?.IsInitOnly ?? false)) return null;
        return target;
    }

    /// <summary><c>public T P { get; set; }</c> or <c>{ get; init; }</c> and nothing else.</summary>
    public static bool IsBareAutoProperty(PropertyDeclarationSyntax syntax)
    {
        if (syntax.AttributeLists.Count > 0 || syntax.Initializer is not null || syntax.ExpressionBody is not null) return false;
        if (syntax.ExplicitInterfaceSpecifier is not null) return false;
        if (syntax.Modifiers.Count != 1 || !syntax.Modifiers[0].IsKind(SyntaxKind.PublicKeyword)) return false;
        if (syntax.AccessorList is not { Accessors.Count: 2 } accessors) return false;
        if (accessors.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null || a.Modifiers.Count > 0 || a.AttributeLists.Count > 0)) return false;
        return accessors.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration))
            && accessors.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration) || a.IsKind(SyntaxKind.InitAccessorDeclaration));
    }
}
