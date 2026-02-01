using System.Security.Cryptography;

namespace MintPlayer.FolderHasher.Abstractions;

/// <summary>
/// Provides methods for computing deterministic hash values of folder contents.
/// </summary>
/// <remarks>
/// The hash is computed based on file paths (sorted alphabetically, case-insensitive) and file contents.
/// Supports .hasherignore files for excluding files from the hash calculation.
/// </remarks>
public interface IFolderHasher
{
    /// <summary>
    /// Computes a SHA256 hash of the folder contents.
    /// </summary>
    /// <param name="folder">The absolute path to the folder to hash.</param>
    /// <returns>A 64-character lowercase hexadecimal string representing the SHA256 hash.</returns>
    /// <remarks>
    /// This method respects .hasherignore files found in the folder and its subdirectories.
    /// Inaccessible files and directories are silently skipped.
    /// </remarks>
    Task<string> GetFolderHashAsync(string folder);

    /// <summary>
    /// Computes a SHA256 hash of the folder contents, excluding folders matching the specified patterns.
    /// </summary>
    /// <param name="folder">The absolute path to the folder to hash.</param>
    /// <param name="ignoreFolders">
    /// A collection of folder name patterns to exclude. Each pattern is matched as a word boundary regex.
    /// For example, "node_modules" will match any path containing the folder name "node_modules".
    /// </param>
    /// <returns>A 64-character lowercase hexadecimal string representing the SHA256 hash.</returns>
    Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders);

    /// <summary>
    /// Computes a hash of the folder contents using the specified hash algorithm.
    /// </summary>
    /// <param name="folder">The absolute path to the folder to hash.</param>
    /// <param name="ignoreFolders">
    /// A collection of folder name patterns to exclude. Each pattern is matched as a word boundary regex.
    /// </param>
    /// <param name="algorithm">
    /// The hash algorithm to use (e.g., SHA256, SHA512, MD5).
    /// The algorithm instance is used directly and should not be reused after this call.
    /// </param>
    /// <returns>A lowercase hexadecimal string representing the computed hash.</returns>
    Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders, HashAlgorithm algorithm);
}
