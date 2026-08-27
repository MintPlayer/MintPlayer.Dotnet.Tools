namespace MintPlayer.Assertions.Formatting;

/// <summary>
/// Glob-style matching used by string Match() and exception WithMessage():
/// <c>*</c> matches any sequence (including empty, spanning newlines), <c>?</c> matches exactly one character.
/// </summary>
public static class WildcardPattern
{
    public static bool IsMatch(string? input, string pattern, bool ignoreCase = false)
    {
        if (input is null) return false;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return IsMatchCore(input.AsSpan(), pattern.AsSpan(), comparison);
    }

    private static bool IsMatchCore(ReadOnlySpan<char> input, ReadOnlySpan<char> pattern, StringComparison comparison)
    {
        // Iterative two-pointer algorithm with backtracking over the last '*'
        int i = 0, p = 0, starP = -1, starI = 0;
        while (i < input.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || CharEquals(input[i], pattern[p], comparison)))
            {
                i++; p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++;
                starI = i;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                i = ++starI;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private static bool CharEquals(char a, char b, StringComparison comparison)
        => comparison == StringComparison.OrdinalIgnoreCase
            ? char.ToUpperInvariant(a) == char.ToUpperInvariant(b)
            : a == b;
}
