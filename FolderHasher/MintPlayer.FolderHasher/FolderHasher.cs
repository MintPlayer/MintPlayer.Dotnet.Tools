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

        // Filter files: exclude ignored files, .hasherignore files, and regex-matched folders.
        //
        // Ordered by the NORMALISED relative path with an ordinal comparer, because the order
        // files are fed in is part of the hash. Sorting the raw path reintroduces exactly the
        // cross-platform divergence that normalising the separator removes: given "sub/b.txt" and
        // a sibling "subX.txt", '/' (0x2F) sorts before 'X' while '\' (0x5C) sorts after, so the
        // same tree feeds in a different order on Windows than on Linux. The default comparer is
        // culture-sensitive too, which makes the ordering depend on the machine's locale.
        var filesToHash = allFiles
            .Where(f => !Path.GetFileName(f).Equals(HasherIgnoreFileName, StringComparison.OrdinalIgnoreCase))
            .Where(f => !ignoreRegex.Any(rgx => rgx.IsMatch(f)))
            .Where(f => !ignoreParser.IsIgnored(f))
            .OrderBy(f => NormalizeRelativePath(f, folder), StringComparer.Ordinal)
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
                // Hash the relative path, normalised so the same tree hashes the same everywhere.
                // See NormalizeRelativePath for why both halves of that normalisation matter.
                var pathBytes = Encoding.UTF8.GetBytes(NormalizeRelativePath(file, folder));
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

    /// <summary>
    /// A file's path relative to <paramref name="folder"/>, in the one form the hash is defined
    /// over: forward slashes, invariant lowercase.
    /// </summary>
    /// <remarks>
    /// Used for BOTH the hashed bytes and the sort order, because the order files are fed into the
    /// algorithm is part of the result. Normalising one and not the other leaves the hash
    /// platform-dependent through the back door.
    ///
    /// Forward slashes: <see cref="string.Substring(int)"/> hands back the OS separator, so
    /// "sub\b.txt" and "sub/b.txt" — the same file — hashed differently on Windows and Linux.
    /// This value is a CACHE KEY, so the effect was silent: a Windows developer and a Linux CI
    /// runner could never share an entry and every lookup missed.
    ///
    /// Invariant lowercase: the culture-sensitive overload maps 'I' to 'ı' under a Turkish locale,
    /// making the hash depend on the machine's regional settings.
    /// </remarks>
    private static string NormalizeRelativePath(string file, string folder)
        => file.Substring(folder.Length + 1)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .ToLowerInvariant();

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
