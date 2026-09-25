namespace MintPlayer.SourceGenerators.Tools;

/// <summary>
/// Use this class instead of the <see cref="Microsoft.CodeAnalysis.Location"/>
/// to ensure that incremental-pipeline models can safely compare on location equality.
/// </summary>
/// <remarks>
/// Equality is by value (ordinal file path plus the four line/column numbers), so a model holding a
/// <see cref="LocationKey"/> stays equal to its previous run under <see cref="EqualityComparer{T}.Default"/>.
/// </remarks>
public sealed class LocationKey : IEquatable<LocationKey>
{
    internal LocationKey(string? filePath, int startLine = default, int startColumn = default, int endLine = default, int endColumn = default)
    {
        FilePath = filePath;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public string? FilePath { get; }
    public int StartLine { get; }
    public int StartColumn { get; }
    public int EndLine { get; }
    public int EndColumn { get; }

    public static readonly LocationKey Null = new(null);

    public bool Equals(LocationKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
            && StartLine == other.StartLine
            && StartColumn == other.StartColumn
            && EndLine == other.EndLine
            && EndColumn == other.EndColumn;
    }

    public override bool Equals(object? obj) => Equals(obj as LocationKey);

    public override int GetHashCode()
    {
        var h = 17;
        h = ValueEquality.Combine(h, FilePath is null ? 0 : StringComparer.Ordinal.GetHashCode(FilePath));
        h = ValueEquality.Combine(h, StartLine);
        h = ValueEquality.Combine(h, StartColumn);
        h = ValueEquality.Combine(h, EndLine);
        h = ValueEquality.Combine(h, EndColumn);
        return h;
    }
}
