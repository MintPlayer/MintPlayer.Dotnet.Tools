using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace MintPlayer.SourceGenerators.Tools.Extensions;

public static class LocationExtensions
{
    public static Location FromSymbol(this ISymbol symbol)
    {
        return symbol.Locations.First();
    }

    public static LocationKey AsKey(this Location? loc)
    {
        if (loc is null || loc is { Kind: LocationKind.None }) return LocationKey.Null;

        var span = loc.GetLineSpan();
        var path = span.Path ?? string.Empty;
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;

        return new LocationKey(path,
            start.Line, start.Character,
            end.Line, end.Character);
    }

    public static Location? ToLocation(this LocationKey? key, Compilation compilation)
    {
        if (key is not LocationKey lk) return null;

        // Find the syntax tree by path
        var tree = compilation.SyntaxTrees.FirstOrDefault(t => StringComparer.OrdinalIgnoreCase.Equals(t.FilePath, lk.FilePath));
        if (tree is null) return null;

        // Map (line, column) -> TextSpan
        var text = tree.GetText();
        var start = text.Lines[lk.StartLine].Start + lk.StartColumn;
        var end = text.Lines[lk.EndLine].Start + lk.EndColumn;
        var span = TextSpan.FromBounds(start, end);

        // Produce a Location for this tree/span
        return Location.Create(tree, span);
    }
}
