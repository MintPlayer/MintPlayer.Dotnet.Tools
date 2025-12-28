using Microsoft.Extensions.FileSystemGlobbing;

namespace MintPlayer.FolderHasher;

/// <summary>
/// Parses .hasherignore files (similar to .gitignore format) and determines if paths should be ignored.
/// Uses glob patterns for matching.
/// </summary>
internal class HasherIgnoreParser
{
    private readonly List<IgnoreRule> _rules = [];

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
