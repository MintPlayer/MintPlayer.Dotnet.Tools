using System.Security.Cryptography;

namespace MintPlayer.FolderHasher.Abstractions;

public interface IFolderHasher
{
    Task<string> GetFolderHashAsync(string folder);
    Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders);
    Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders, HashAlgorithm algorithm);
}
