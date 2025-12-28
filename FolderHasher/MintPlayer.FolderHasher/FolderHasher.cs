using MintPlayer.FolderHasher.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MintPlayer.FolderHasher;

internal class FolderHasher : IFolderHasher
{
    private const string HasherIgnoreFileName = ".hasherignore";

    public async Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders, HashAlgorithm algorithm)
    {
        var ignoreRegex = ignoreFolders.Select(f => new Regex($@"\b{f}\b")).ToArray();

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

        // Filter files: exclude ignored files, .hasherignore files, and regex-matched folders
        var filesToHash = allFiles
            .Where(f => !Path.GetFileName(f).Equals(HasherIgnoreFileName, StringComparison.OrdinalIgnoreCase))
            .Where(f => !ignoreRegex.Any(rgx => rgx.IsMatch(f)))
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

        // Read all files in parallel
        var files = await Task.WhenAll(
            filesToHash.Select(async f => new { ContentBytes = await File.ReadAllBytesAsync(f), Path = f })
        );

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

    public async Task<string> GetFolderHashAsync(string folder)
    {
        var hash = await GetFolderHashAsync(folder, [], SHA256.Create());
        return hash;
    }

    public async Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders)
    {
        var hash = await GetFolderHashAsync(folder, ignoreFolders, SHA256.Create());
        return hash;
    }
}
