using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools.Extensions;

public static class SymbolExtensions
{
    public static IEnumerable<IPropertySymbol> GetAllProperties(this INamedTypeSymbol? typeSymbol)
    {
        if (typeSymbol is null) return [];

        return typeSymbol.GetAllBaseTypes()
            .SelectMany(t => t.GetMembers().OfType<IPropertySymbol>());
    }

    public static IEnumerable<INamedTypeSymbol> GetAllBaseTypes(this INamedTypeSymbol? typeSymbol)
    {
        while (typeSymbol is { })
        {
            yield return typeSymbol;
            typeSymbol = typeSymbol.BaseType;
        }
    }

    public static PathSpec? GetPathSpec(this INamedTypeSymbol typeSymbol)
    {
        var parents = new List<PathSpecElement>();
        var ns = string.Empty;
        while (typeSymbol is { })
        {
            ns = typeSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
            if (typeSymbol.ContainingSymbol is INamedTypeSymbol namedTypeSymbol &&
                    new[] { TypeKind.Class, TypeKind.Struct }.Contains(namedTypeSymbol.TypeKind))
                parents.Add(new PathSpecElement { Name = namedTypeSymbol.Name, Type = namedTypeSymbol.TypeKind switch { TypeKind.Class => EPathSpecType.Class, _ => EPathSpecType.Struct } });
            //else
            //    return null;

            typeSymbol = typeSymbol.ContainingType;
        }

        return new PathSpec
        {
            ContainingNamespace = ns,
            Parents = parents.ToArray(),
        };
    }
}

[ValueComparer(typeof(PathSpecValueComparer))]
public class PathSpec
{
    public string? ContainingNamespace { get; set; }
    public PathSpecElement[] Parents { get; set; }
}

[ValueComparer(typeof(PathSpecElementValueComparer))]
public class PathSpecElement
{
    public string? Name { get; set; }
    public EPathSpecType Type { get; set; }
}

public enum EPathSpecType
{
    Class,
    Struct,
}