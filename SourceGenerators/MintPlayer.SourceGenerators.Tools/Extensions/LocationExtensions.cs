using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools.Extensions;

public static class LocationExtensions
{
    public static Location FromSymbol(this ISymbol symbol)
    {
        return symbol.Locations.First();
    }
}
