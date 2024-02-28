using MintPlayer.StringBuilder.Extensions.SplitLines;
using System.Linq;

namespace MintPlayer.StringBuilder.Extensions;

public static class StringExtensions
{
    public static LineSplitEnumerator SplitLines(this string value)
    {
        // LineSplitEnumerator is a struct so there is no allocation here
        return new LineSplitEnumerator(value.AsSpan());
    }

    public static string Dedent(this string value)
    {
        var lines = value.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        var lastLine = lines[^1].Replace("\t", "    ");
        if (lastLine.Any(c => c != ' '))
            throw new InvalidOperationException();
        
        var indentSize = lastLine.Length;

        var result = string.Join(Environment.NewLine, lines.ToArray().Select((line, index) => index == 0 ? line : DedentLine(line, indentSize)));
        return result;
    }

    private static string DedentLine(this string line, int spaces)
    {
        var lineSpan = line.AsSpan();
        var trimmedChars = 0;
        var trimmedSpaces = 0;
        foreach (var c in lineSpan)
        {
            trimmedChars++;
            switch (c)
            {
                case ' ':
                    trimmedSpaces++;
                    break;
                case '\t':
                    trimmedSpaces += 4;
                    break;
                default:
                    throw new Exception($@"Line ""{line}"" contains too few spaces at the start (should start with {spaces} spaces).");
            }

            if (trimmedSpaces >= spaces)
            {
                var spaceStr = string.Concat(Enumerable.Repeat(" ", trimmedSpaces - spaces));
                return spaceStr + lineSpan.Slice(trimmedChars).ToString();
            }
        }
        return string.Empty;
    }
}
