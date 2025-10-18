namespace MintPlayer.SourceGenerators.Tools;

/// <summary>
/// Use this struct instead of the <see cref="Microsoft.CodeAnalysis.Location"/>
/// to ensure that value-comparers can safely filter on location equality.
/// </summary>
[ValueComparer(typeof(ValueComparers.LocationKeyValueComparer))]
public class LocationKey
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
}