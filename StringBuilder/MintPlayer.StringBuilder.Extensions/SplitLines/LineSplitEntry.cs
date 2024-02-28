namespace MintPlayer.StringBuilder.Extensions.SplitLines;

public readonly ref struct LineSplitEntry
{
    public LineSplitEntry(ReadOnlySpan<char> line, ReadOnlySpan<char> separator)
    {
        Line = line;
        Separator = separator;
    }

    public ReadOnlySpan<char> Line { get; }
    public ReadOnlySpan<char> Separator { get; }

    // This method allow to deconstruct the type, so you can write any of the following code
    // foreach (var entry in str.SplitLines()) { _ = entry.Line; }
    // foreach (var (line, endOfLine) in str.SplitLines()) { _ = line; }
    // https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/functional/deconstruct?WT.mc_id=DT-MVP-5003978#deconstructing-user-defined-types
    public void Deconstruct(out ReadOnlySpan<char> line, out ReadOnlySpan<char> separator)
    {
        line = Line;
        separator = Separator;
    }

    // This method allow to implicitly cast the type into a ReadOnlySpan<char>, so you can write the following code
    // foreach (ReadOnlySpan<char> entry in str.SplitLines())
    public static implicit operator ReadOnlySpan<char>(LineSplitEntry entry) => entry.Line;
}