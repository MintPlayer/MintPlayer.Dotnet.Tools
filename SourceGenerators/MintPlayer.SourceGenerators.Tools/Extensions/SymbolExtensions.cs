using Microsoft.CodeAnalysis;

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
}
