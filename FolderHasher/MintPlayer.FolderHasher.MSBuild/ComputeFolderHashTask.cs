using Microsoft.Build.Framework;
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
    private const long LargeFileThreshold = 10 * 1024 * 1024; // 10MB
    private const int StreamBufferSize = 81920; // 80KB buffer for streaming

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

    private string ComputeFolderHash(string folder)
    {
        using var algorithm = SHA256.Create();

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
                Log.LogMessage(MessageImportance.Low, $"Skipping inaccessible ignore file: {ignoreFile}");
            }
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
                HashFileContents(file, algorithm, isLastFile);
            }
            catch (Exception ex) when (IsAccessException(ex))
            {
                Log.LogMessage(MessageImportance.Low, $"Skipping inaccessible file: {file}");

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

    private List<string> GetAllFilesWithAccessHandling(string folder)
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
                Log.LogMessage(MessageImportance.Low, $"Skipping inaccessible directory: {currentDir}");
            }
        }

        return files;
    }

    private static void HashFileContents(string filePath, HashAlgorithm algorithm, bool isLastFile)
    {
        var fileInfo = new FileInfo(filePath);

        if (fileInfo.Length > LargeFileThreshold)
        {
            // Stream large files in chunks
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize);
            var buffer = new byte[StreamBufferSize];
            int bytesRead;

            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
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
            var contentBytes = File.ReadAllBytes(filePath);

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
