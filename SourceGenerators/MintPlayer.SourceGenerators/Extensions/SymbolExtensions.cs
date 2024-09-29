using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Extensions
{
    internal static class SymbolExtensions
    {
        public static string ToFullyQualifiedName(this ITypeSymbol symbol)
            => symbol.ToDisplayString(new SymbolDisplayFormat(
                    globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
                ));
    }
}
