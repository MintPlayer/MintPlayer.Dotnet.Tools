using Microsoft.Extensions.FileSystemGlobbing;

namespace MintPlayer.FolderHasher;

/// <summary>
/// Parses .hasherignore files (similar to .gitignore format) and determines if paths should be ignored.
/// Uses glob patterns for matching.
/// </summary>
/// <remarks>
/// <para>Supported pattern syntax:</para>
/// <list type="bullet">
/// <item><description><c>*.log</c> - Matches files with .log extension in any directory</description></item>
/// <item><description><c>node_modules/</c> - Matches the node_modules directory at root level</description></item>
/// <item><description><c>**/temp/</c> - Matches temp directory anywhere in the tree</description></item>
/// <item><description><c>/build</c> - Matches only at root (leading slash)</description></item>
/// <item><description><c>!important.log</c> - Negation pattern (excludes from ignore)</description></item>
/// <item><description><c># comment</c> - Lines starting with # are comments</description></item>
/// </list>
/// </remarks>
public class HasherIgnoreParser
{
    private readonly List<IgnoreRule> _rules = [];

    /// <summary>
    /// Adds a single ignore pattern.
    /// </summary>
    /// <param name="pattern">The glob pattern to add. Comments (starting with #) and empty lines are ignored.</param>
    /// <param name="basePath">The base path that this pattern applies to (typically the directory containing the .hasherignore file).</param>
    public void AddPattern(string pattern, string basePath)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.StartsWith('#'))
            return;

        var isNegation = pattern.StartsWith('!');
        if (isNegation)
            pattern = pattern[1..];

        pattern = pattern.Trim();
        if (string.IsNullOrEmpty(pattern))
            return;

        // Normalize the pattern for glob matching
        var normalizedPattern = NormalizePattern(pattern);

        _rules.Add(new IgnoreRule(normalizedPattern, isNegation, NormalizePath(basePath)));
    }

    /// <summary>
    /// Reads and parses all patterns from a .hasherignore file.
    /// </summary>
    /// <param name="ignoreFilePath">The absolute path to the .hasherignore file.</param>
    /// <remarks>
    /// If the file does not exist, this method does nothing (no exception is thrown).
    /// The base path for pattern matching is derived from the file's directory.
    /// </remarks>
    public void AddPatternsFromFile(string ignoreFilePath)
    {
        if (!File.Exists(ignoreFilePath))
            return;

        var basePath = Path.GetDirectoryName(ignoreFilePath) ?? string.Empty;
        var lines = File.ReadAllLines(ignoreFilePath);

        foreach (var line in lines)
        {
            AddPattern(line, basePath);
        }
    }

    /// <summary>
    /// Determines whether the specified path should be ignored based on the configured patterns.
    /// </summary>
    /// <param name="path">The absolute path to check.</param>
    /// <returns><c>true</c> if the path matches an ignore pattern (and is not negated); otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// Patterns are evaluated in order. Later patterns can override earlier ones.
    /// Negation patterns (starting with !) can un-ignore previously ignored paths.
    /// </remarks>
    public bool IsIgnored(string path)
    {
        var normalizedPath = NormalizePath(path);
        var isIgnored = false;

        foreach (var rule in _rules)
        {
            if (MatchesPattern(normalizedPath, rule))
            {
                isIgnored = !rule.IsNegation;
            }
        }

        return isIgnored;
    }

    private static bool MatchesPattern(string filePath, IgnoreRule rule)
    {
        // Get the relative path from the base path
        var basePath = rule.BasePath;
        if (!filePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            return false;

        var relativePath = filePath.Length > basePath.Length
            ? filePath[(basePath.Length + 1)..]
            : string.Empty;

        if (string.IsNullOrEmpty(relativePath))
            return false;

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(rule.Pattern);

        // Check if the file matches the pattern
        var result = matcher.Match(relativePath);
        if (result.HasMatches)
            return true;

        // Also check if any parent directory matches (for directory patterns)
        var pathParts = relativePath.Split('/');
        for (var i = 1; i < pathParts.Length; i++)
        {
            var partialPath = string.Join("/", pathParts.Take(i));
            result = matcher.Match(partialPath);
            if (result.HasMatches)
                return true;
        }

        return false;
    }

    private static string NormalizePattern(string pattern)
    {
        // Remove leading slash (patterns are relative to .hasherignore location)
        if (pattern.StartsWith('/'))
            pattern = pattern[1..];

        // Handle directory-only patterns (ending with /)
        if (pattern.EndsWith('/'))
            pattern = pattern[..^1] + "/**";

        // If pattern doesn't contain a slash, it matches in any subdirectory
        if (!pattern.Contains('/'))
            pattern = "**/" + pattern;

        return pattern;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/');
    }

    private record IgnoreRule(string Pattern, bool IsNegation, string BasePath);
}
