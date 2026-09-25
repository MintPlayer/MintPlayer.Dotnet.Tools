using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.ValueComparerGenerator.Models;

/// <summary>What discovery found for one type: the model to generate from (if any), and its diagnostics.</summary>
/// <remarks>
/// The two are kept apart so that the producer step, which selects only <see cref="Model"/>, stays Unchanged when an
/// edit above the type moves a diagnostic's span but changes nothing that is generated.
/// </remarks>
public sealed class DiscoveredType : IEquatable<DiscoveredType>
{
    /// <summary>Null when nothing is generated for the type, for instance because it is not partial.</summary>
    public ClassDeclaration? Model { get; set; }

    public EquatableArray<DiagnosticInfo> Diagnostics { get; set; } = EquatableArray<DiagnosticInfo>.Empty;

    public bool Equals(DiscoveredType? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Equals(Model, other.Model) && Diagnostics.Equals(other.Diagnostics);
    }

    public override bool Equals(object? obj) => Equals(obj as DiscoveredType);

    public override int GetHashCode() => ValueEquality.Combine(Model?.GetHashCode() ?? 0, Diagnostics.GetHashCode());

    public override string ToString() => Model?.ToString() ?? "(no model)";
}

/// <summary>A diagnostic to report, without a <see cref="Location"/> or any other compilation-bound object.</summary>
public sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    /// <summary>The key of the descriptor in <see cref="Generators.ValueComparerDiagnostics"/>.</summary>
    public string Rule { get; set; } = string.Empty;

    public DiagnosticLocation? Location { get; set; }

    public EquatableArray<string> MessageArgs { get; set; } = EquatableArray<string>.Empty;

    public bool Equals(DiagnosticInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Rule, other.Rule, StringComparison.Ordinal)
            && Equals(Location, other.Location)
            && MessageArgs.Equals(other.MessageArgs);
    }

    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    public override int GetHashCode()
        => ValueEquality.Combine(ValueEquality.Combine(StringComparer.Ordinal.GetHashCode(Rule), Location?.GetHashCode() ?? 0), MessageArgs.GetHashCode());

    public override string ToString() => $"{Rule} {string.Join(", ", MessageArgs)}";
}

/// <summary>An equatable file and line span. Only diagnostic cases carry one, so ordinary models stay location-free.</summary>
public sealed class DiagnosticLocation : IEquatable<DiagnosticLocation>
{
    public string FilePath { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }

    public static DiagnosticLocation? From(Location? location)
    {
        if (location is null || !location.IsInSource) return null;
        var span = location.GetLineSpan();
        return new DiagnosticLocation
        {
            FilePath = span.Path ?? string.Empty,
            StartLine = span.StartLinePosition.Line,
            StartColumn = span.StartLinePosition.Character,
            EndLine = span.EndLinePosition.Line,
            EndColumn = span.EndLinePosition.Character,
        };
    }

    /// <summary>
    /// Maps the span back onto the compilation's own tree, so <c>#pragma</c> and <c>.editorconfig</c> severity
    /// apply to the diagnostic.
    /// </summary>
    public Location ToLocation(Compilation compilation)
    {
        var tree = compilation.SyntaxTrees.FirstOrDefault(t => string.Equals(t.FilePath, FilePath, StringComparison.OrdinalIgnoreCase));
        var lineSpan = new LinePositionSpan(new LinePosition(StartLine, StartColumn), new LinePosition(EndLine, EndColumn));
        if (tree is null) return Location.Create(FilePath, default, lineSpan);

        var text = tree.GetText();
        if (StartLine >= text.Lines.Count || EndLine >= text.Lines.Count) return Location.Create(FilePath, default, lineSpan);
        var start = text.Lines[StartLine].Start + StartColumn;
        var end = text.Lines[EndLine].Start + EndColumn;
        return Location.Create(tree, TextSpan.FromBounds(start, Math.Min(end, text.Length)));
    }

    public bool Equals(DiagnosticLocation? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
            && StartLine == other.StartLine
            && StartColumn == other.StartColumn
            && EndLine == other.EndLine
            && EndColumn == other.EndColumn;
    }

    public override bool Equals(object? obj) => Equals(obj as DiagnosticLocation);

    public override int GetHashCode()
    {
        var h = StringComparer.Ordinal.GetHashCode(FilePath);
        h = ValueEquality.Combine(h, StartLine);
        h = ValueEquality.Combine(h, StartColumn);
        h = ValueEquality.Combine(h, EndLine);
        h = ValueEquality.Combine(h, EndColumn);
        return h;
    }

    public override string ToString() => $"{FilePath}({StartLine + 1},{StartColumn + 1})";
}
