using Microsoft.Build.Framework;
using Microsoft.Extensions.FileSystemGlobbing;
using System.Security.Cryptography;
using System.Text;

namespace MintPlayer.FolderHasher.MSBuild;

/// <summary>
/// MSBuild task that computes a hash of a folder's contents.
/// Supports .hasherignore files for excluding files from the hash calculation.
/// </summary>
public class ComputeFolderHashTask : Microsoft.Build.Utilities.Task
{
    private const string HasherIgnoreFileName = ".hasherignore";

    /// <summary>
    /// The path to the folder to hash.
    /// </summary>
    [Required]
    public string FolderPath { get; set; } = "";

    /// <summary>
    /// The computed hash of the folder contents.
    /// </summary>
    [Output]
    public string Hash { get; private set; } = "";

    public override bool Execute()
    {
        try
        {
            if (!Directory.Exists(FolderPath))
            {
                Log.LogError($"Folder not found: {FolderPath}");
                return false;
            }

            Hash = ComputeFolderHash(FolderPath);
            Log.LogMessage(MessageImportance.Normal, $"Computed folder hash for '{FolderPath}': {Hash}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private static string ComputeFolderHash(string folder)
    {
        using var algorithm = SHA256.Create();

        // Build the ignore parser from all .hasherignore files
        var ignoreParser = new HasherIgnoreParser();
        var allFiles = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);

        // Find and process all .hasherignore files first
        var hasherIgnoreFiles = allFiles
            .Where(f => Path.GetFileName(f).Equals(HasherIgnoreFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Length) // Process parent directories first
            .ToList();

        foreach (var ignoreFile in hasherIgnoreFiles)
        {
            ignoreParser.AddPatternsFromFile(ignoreFile);
        }

        // Filter files: exclude ignored files and .hasherignore files
        var filesToHash = allFiles
            .Where(f => !Path.GetFileName(f).Equals(HasherIgnoreFileName, StringComparison.OrdinalIgnoreCase))
            .Where(f => !ignoreParser.IsIgnored(f))
            .OrderBy(p => p)
            .ToList();

        if (filesToHash.Count == 0)
        {
            // No files to hash - return hash of empty content
            algorithm.TransformFinalBlock([], 0, 0);
            if (algorithm.Hash == null)
                throw new InvalidOperationException("Could not determine folder hash");
            return Convert.ToHexStringLower(algorithm.Hash);
        }

        // Read all files
        var files = filesToHash
            .Select(f => new { ContentBytes = File.ReadAllBytes(f), Path = f })
            .ToArray();

        foreach (var file in files)
        {
            // hash path
            var relativePath = file.Path.Substring(folder.Length + 1);
            var pathBytes = Encoding.UTF8.GetBytes(relativePath.ToLower());
            algorithm.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);

            // hash contents
            var contentBytes = file.ContentBytes;
            if (ReferenceEquals(file, files[files.Length - 1]))
                algorithm.TransformFinalBlock(contentBytes, 0, contentBytes.Length);
            else
                algorithm.TransformBlock(contentBytes, 0, contentBytes.Length, contentBytes, 0);
        }

        if (algorithm.Hash == null)
            throw new InvalidOperationException("Could not determine folder hash");

        return Convert.ToHexStringLower(algorithm.Hash);
    }
}

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
