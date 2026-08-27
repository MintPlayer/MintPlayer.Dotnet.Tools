using System.Text;

namespace MintPlayer.Assertions.Formatting;

/// <summary>
/// Builds the "they differ at index N" hint used by string equality assertions: a windowed
/// excerpt of both strings around the first differing character, so long strings still
/// produce a failure message that pinpoints the mismatch.
/// </summary>
internal static class StringDifference
{
    private const int ContextBefore = 10;
    private const int ContextAfter = 15;

    /// <summary>
    /// Returns a fragment like <c>they differ at index 4: "abcDe…" vs "abcXe…"</c> for two
    /// non-equal strings. When one string is a prefix of the other, the index is the length
    /// of the shorter string.
    /// </summary>
    public static string Describe(string actual, string expected)
    {
        var index = IndexOfFirstMismatch(actual, expected);
        return $"they differ at index {index}: {Excerpt(actual, index)} vs {Excerpt(expected, index)}";
    }

    /// <summary>The index of the first character that differs (ordinal), or the shorter length when one is a prefix of the other.</summary>
    public static int IndexOfFirstMismatch(string left, string right)
    {
        var min = Math.Min(left.Length, right.Length);
        for (var i = 0; i < min; i++)
        {
            if (left[i] != right[i]) return i;
        }
        return min;
    }

    private static string Excerpt(string value, int index)
    {
        var start = Math.Max(0, index - ContextBefore);
        var end = Math.Min(value.Length, index + ContextAfter);

        var sb = new StringBuilder(end - start + 8);
        sb.Append('"');
        if (start > 0) sb.Append('…');
        for (var i = start; i < end; i++)
        {
            switch (value[i])
            {
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(value[i]); break;
            }
        }
        if (end < value.Length) sb.Append('…');
        sb.Append('"');
        return sb.ToString();
    }
}
