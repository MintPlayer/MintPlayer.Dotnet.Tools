using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace MintPlayer.SourceGenerators.Tools;

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

    /// <summary>
    /// Maps a stored <see cref="LocationKey"/> back onto the compilation's own syntax tree, so <c>#pragma</c> and
    /// <c>.editorconfig</c> severity apply to a diagnostic reported there.
    /// </summary>
    /// <returns>
    /// Null when there is no key, no tree with that path, or the span no longer fits the tree's text (a stale key,
    /// or two trees with the same path). Report at <see cref="Location.None"/> then; never index past the text.
    /// </returns>
    public static Location? ToLocation(this LocationKey? key, Compilation compilation)
    {
        if (key is not LocationKey lk || lk.FilePath is null || compilation is null) return null;

        // Find the syntax tree by path
        var tree = compilation.SyntaxTrees.FirstOrDefault(t => StringComparer.OrdinalIgnoreCase.Equals(t.FilePath, lk.FilePath));
        if (tree is null) return null;

        // Map (line, column) -> TextSpan. Every index is checked: an exception here used to escape
        // ReportDiagnostics and take the whole generator down (CS8785).
        var text = tree.GetText();
        if (lk.StartLine < 0 || lk.EndLine < 0 || lk.StartColumn < 0 || lk.EndColumn < 0) return null;
        if (lk.StartLine >= text.Lines.Count || lk.EndLine >= text.Lines.Count) return null;

        var startLine = text.Lines[lk.StartLine];
        var endLine = text.Lines[lk.EndLine];
        if (lk.StartColumn > startLine.EndIncludingLineBreak - startLine.Start) return null;
        if (lk.EndColumn > endLine.EndIncludingLineBreak - endLine.Start) return null;

        var start = startLine.Start + lk.StartColumn;
        var end = endLine.Start + lk.EndColumn;
        if (end < start || end > text.Length) return null;
        var span = TextSpan.FromBounds(start, end);

        // Produce a Location for this tree/span
        return Location.Create(tree, span);
    }
}
