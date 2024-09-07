namespace MintPlayer.StringExtensions;

public static class SplitLinesExtensions
{
    public static IEnumerable<string> SplitLines(string input, params string[] lineEndings)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Enumerable.Empty<string>();

        var lines = input.Split(lineEndings, StringSplitOptions.None);
        return lines.Length == 0 ? Enumerable.Empty<string>() : lines;
    }
}
