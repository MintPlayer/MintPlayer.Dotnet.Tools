using System.Text.RegularExpressions;

namespace MintPlayer.TokenReplacer.Targets;

/// <summary>
/// Result of a <see cref="TokenReplacementEngine.Replace"/> call.
/// </summary>
public sealed class TokenReplacementResult
{
    internal TokenReplacementResult(string content, int replacedCount, IReadOnlyList<string> unmatchedTokens)
    {
        Content = content;
        ReplacedCount = replacedCount;
        UnmatchedTokens = unmatchedTokens;
    }

    /// <summary>The content with all known tokens replaced.</summary>
    public string Content { get; }

    /// <summary>Total number of token occurrences that were replaced.</summary>
    public int ReplacedCount { get; }

    /// <summary>Distinct token names found in the content that had no configured value (left as-is).</summary>
    public IReadOnlyList<string> UnmatchedTokens { get; }
}

/// <summary>
/// Pure token replacement logic, independent of MSBuild so it can be unit tested directly.
/// Tokens are written as <c>$name$</c> (delimiters configurable) where the name consists of
/// letters, digits, '_', '.' and '-'. Constructs like <c>$(Property)</c> are never treated as tokens.
/// </summary>
public static class TokenReplacementEngine
{
    private const string TokenNamePattern = @"([A-Za-z0-9_.\-]+)";

    /// <summary>
    /// Replaces all known tokens in <paramref name="content"/>. Unknown tokens are left untouched
    /// and reported in <see cref="TokenReplacementResult.UnmatchedTokens"/>.
    /// </summary>
    /// <param name="content">The text to process.</param>
    /// <param name="tokens">Token name → replacement value. Pass a case-insensitive dictionary for case-insensitive token matching.</param>
    /// <param name="tokenStart">Opening delimiter, default <c>$</c>.</param>
    /// <param name="tokenEnd">Closing delimiter, default <c>$</c>.</param>
    public static TokenReplacementResult Replace(string content, IReadOnlyDictionary<string, string> tokens, string tokenStart = "$", string tokenEnd = "$")
    {
        if (content == null) throw new ArgumentNullException(nameof(content));
        if (tokens == null) throw new ArgumentNullException(nameof(tokens));
        if (string.IsNullOrEmpty(tokenStart)) throw new ArgumentException("Token start delimiter must not be empty.", nameof(tokenStart));
        if (string.IsNullOrEmpty(tokenEnd)) throw new ArgumentException("Token end delimiter must not be empty.", nameof(tokenEnd));

        var pattern = Regex.Escape(tokenStart) + TokenNamePattern + Regex.Escape(tokenEnd);
        var unmatched = new List<string>();
        var replaced = 0;

        var result = Regex.Replace(content, pattern, match =>
        {
            var name = match.Groups[1].Value;
            if (tokens.TryGetValue(name, out var value))
            {
                replaced++;
                return value;
            }

            if (!unmatched.Contains(name))
                unmatched.Add(name);
            return match.Value;
        });

        return new TokenReplacementResult(result, replaced, unmatched);
    }
}
