namespace MintPlayer.Assertions.Primitives;

/// <summary>
/// How a string comparison should treat casing, whitespace and newline style.
/// </summary>
/// <remarks>
/// <para>
/// FluentAssertions spells this as a per-call options object built by a lambda
/// (<c>o =&gt; o.IgnoringCase().IgnoringNewlineStyle()</c>). That shape allocates a builder and runs a
/// delegate on the passing path of every call that uses it, which §0 of the Phase 2 PRD rules out.
/// A flags enum says the same thing, costs nothing, and is a compile-time constant at every call
/// site.
/// </para>
/// <para>
/// Only <see cref="IgnoringCase"/> is genuinely free — it maps onto
/// <see cref="StringComparison.OrdinalIgnoreCase"/>, which no string has to be rewritten for. Every
/// other flag requires normalising both sides, which allocates. That is inherent to the question
/// being asked, not an implementation choice, but it is worth knowing before reaching for these in a
/// tight loop.
/// </para>
/// </remarks>
[Flags]
public enum StringMatchOptions
{
    /// <summary>Ordinal comparison, exactly as the parameterless overloads do it.</summary>
    None = 0,

    /// <summary>Compare ordinal-ignore-case.</summary>
    IgnoringCase = 1,

    /// <summary>Trim whitespace from the start of both sides before comparing.</summary>
    IgnoringLeadingWhitespace = 2,

    /// <summary>Trim whitespace from the end of both sides before comparing.</summary>
    IgnoringTrailingWhitespace = 4,

    /// <summary>Trim whitespace from both ends of both sides. The combination of the two above.</summary>
    IgnoringSurroundingWhitespace = IgnoringLeadingWhitespace | IgnoringTrailingWhitespace,

    /// <summary>
    /// Remove every whitespace character from both sides, not just the surrounding ones.
    /// </summary>
    /// <remarks>
    /// This subsumes the leading/trailing flags and also collapses interior runs to nothing, so
    /// <c>"a b"</c> and <c>"ab"</c> compare equal. Deliberately blunt: a "collapse runs to a single
    /// space" variant reads the same at the call site and means something different, so only the
    /// unambiguous one is offered.
    /// </remarks>
    IgnoringAllWhitespace = 8,

    /// <summary>
    /// Normalise <c>\r\n</c> and a bare <c>\r</c> to <c>\n</c> on both sides before comparing.
    /// </summary>
    /// <remarks>
    /// The flag a test needs when it compares generated output against a checked-in literal and the
    /// repository does not enforce line endings.
    /// </remarks>
    IgnoringNewlineStyle = 16,
}

/// <summary>
/// Applies <see cref="StringMatchOptions"/> to the two sides of a comparison.
/// </summary>
internal static class StringMatch
{
    /// <summary>Everything except <see cref="StringMatchOptions.IgnoringCase"/>, which needs no rewrite.</summary>
    private const StringMatchOptions RewritingFlags =
        StringMatchOptions.IgnoringLeadingWhitespace |
        StringMatchOptions.IgnoringTrailingWhitespace |
        StringMatchOptions.IgnoringAllWhitespace |
        StringMatchOptions.IgnoringNewlineStyle;

    /// <summary>The <see cref="StringComparison"/> the options ask for.</summary>
    public static StringComparison Comparison(StringMatchOptions options)
        => options.HasFlag(StringMatchOptions.IgnoringCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The value to compare, with whitespace and newline normalisation applied. Returns
    /// <paramref name="value"/> itself when no flag asks for a rewrite, so the common case allocates
    /// nothing.
    /// </summary>
    public static string? Normalize(string? value, StringMatchOptions options)
    {
        if (value is null || (options & RewritingFlags) == 0) return value;

        if (options.HasFlag(StringMatchOptions.IgnoringAllWhitespace))
        {
            // Strips newlines too, so IgnoringNewlineStyle has nothing left to do.
            return RemoveWhitespace(value);
        }

        var result = value;

        if (options.HasFlag(StringMatchOptions.IgnoringNewlineStyle))
        {
            result = NormalizeNewlines(result);
        }

        if (options.HasFlag(StringMatchOptions.IgnoringLeadingWhitespace | StringMatchOptions.IgnoringTrailingWhitespace))
        {
            result = result.Trim();
        }
        else if (options.HasFlag(StringMatchOptions.IgnoringLeadingWhitespace))
        {
            result = result.TrimStart();
        }
        else if (options.HasFlag(StringMatchOptions.IgnoringTrailingWhitespace))
        {
            result = result.TrimEnd();
        }

        return result;
    }

    private static string RemoveWhitespace(string value)
    {
        var hasWhitespace = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c)) { hasWhitespace = true; break; }
        }

        if (!hasWhitespace) return value;

        return string.Create(CountNonWhitespace(value), value, static (destination, source) =>
        {
            var written = 0;
            foreach (var c in source)
            {
                if (!char.IsWhiteSpace(c)) destination[written++] = c;
            }
        });
    }

    private static int CountNonWhitespace(string value)
    {
        var count = 0;
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c)) count++;
        }
        return count;
    }

    /// <summary>
    /// <c>\r\n</c> and a lone <c>\r</c> both become <c>\n</c>.
    /// </summary>
    /// <remarks>
    /// Two <see cref="string.Replace(string, string)"/> calls would be shorter and wrong in one case:
    /// replacing <c>\r</c> with <c>\n</c> first turns every <c>\r\n</c> into <c>\n\n</c>. Ordering
    /// fixes that, but a single pass is both correct by construction and one allocation instead of two.
    /// </remarks>
    private static string NormalizeNewlines(string value)
    {
        if (!value.Contains('\r')) return value;

        var builder = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\r')
            {
                builder.Append('\n');
                if (i + 1 < value.Length && value[i + 1] == '\n') i++;
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }
}
