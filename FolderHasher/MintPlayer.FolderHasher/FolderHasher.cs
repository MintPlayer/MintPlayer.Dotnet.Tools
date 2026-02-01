using MintPlayer.FolderHasher.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MintPlayer.FolderHasher;

internal class FolderHasher : IFolderHasher
{
    private const string HasherIgnoreFileName = ".hasherignore";
    private const long LargeFileThreshold = 10 * 1024 * 1024; // 10MB
    private const int StreamBufferSize = 81920; // 80KB buffer for streaming

    public async Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders, HashAlgorithm algorithm)
    {
        var ignoreRegex = ignoreFolders.Select(f => new Regex($@"\b{f}\b")).ToArray();

        // Build the ignore parser from all .hasherignore files
        var ignoreParser = new HasherIgnoreParser();

        // Get all files, handling inaccessible directories
        var allFiles = GetAllFilesWithAccessHandling(folder);

        // Find and process all .hasherignore files first
        var hasherIgnoreFiles = allFiles
            .Where(f => Path.GetFileName(f).Equals(HasherIgnoreFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Length) // Process parent directories first
            .ToList();

        foreach (var ignoreFile in hasherIgnoreFiles)
        {
            try
            {
                ignoreParser.AddPatternsFromFile(ignoreFile);
            }
            catch (Exception ex) when (IsAccessException(ex))
            {
                // Skip inaccessible ignore files silently
            }
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

        // Process files one by one with streaming support for large files
        for (var i = 0; i < filesToHash.Count; i++)
        {
            var file = filesToHash[i];
            var isLastFile = i == filesToHash.Count - 1;

            try
            {
                // Hash the relative path
                var relativePath = file.Substring(folder.Length + 1);
                var pathBytes = Encoding.UTF8.GetBytes(relativePath.ToLower());
                algorithm.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);

                // Hash the file contents
                await HashFileContentsAsync(file, algorithm, isLastFile);
            }
            catch (Exception ex) when (IsAccessException(ex))
            {
                // Skip inaccessible files silently
                // If this was supposed to be the last file, we need to finalize with empty content
                if (isLastFile)
                {
                    algorithm.TransformFinalBlock([], 0, 0);
                }
            }
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

    private static List<string> GetAllFilesWithAccessHandling(string folder)
    {
        var files = new List<string>();
        var directoriesToProcess = new Queue<string>();
        directoriesToProcess.Enqueue(folder);

        while (directoriesToProcess.Count > 0)
        {
            var currentDir = directoriesToProcess.Dequeue();

            try
            {
                // Add files in current directory
                files.AddRange(Directory.GetFiles(currentDir));

                // Queue subdirectories for processing
                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    directoriesToProcess.Enqueue(subDir);
                }
            }
            catch (Exception ex) when (IsAccessException(ex))
            {
                // Skip inaccessible directories silently
            }
        }

        return files;
    }

    private static async Task HashFileContentsAsync(string filePath, HashAlgorithm algorithm, bool isLastFile)
    {
        var fileInfo = new FileInfo(filePath);

        if (fileInfo.Length > LargeFileThreshold)
        {
            // Stream large files in chunks
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, useAsync: true);
            var buffer = new byte[StreamBufferSize];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                // Check if this is the last chunk of the last file
                if (isLastFile && stream.Position >= stream.Length)
                {
                    algorithm.TransformFinalBlock(buffer, 0, bytesRead);
                }
                else
                {
                    algorithm.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                }
            }

            // If the file was empty and this is the last file
            if (isLastFile && fileInfo.Length == 0)
            {
                algorithm.TransformFinalBlock([], 0, 0);
            }
        }
        else
        {
            // Read small files entirely into memory
            var contentBytes = await File.ReadAllBytesAsync(filePath);

            if (isLastFile)
            {
                algorithm.TransformFinalBlock(contentBytes, 0, contentBytes.Length);
            }
            else
            {
                algorithm.TransformBlock(contentBytes, 0, contentBytes.Length, contentBytes, 0);
            }
        }
    }

    private static bool IsAccessException(Exception ex)
    {
        return ex is UnauthorizedAccessException
            || ex is IOException
            || ex is System.Security.SecurityException;
    }
}
