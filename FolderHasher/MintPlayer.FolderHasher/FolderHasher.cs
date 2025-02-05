using MintPlayer.FolderHasher.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MintPlayer.FolderHasher;

internal class FolderHasher : IFolderHasher
{
    public async Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders, HashAlgorithm algorithm)
    {
        var ignoreRegex = ignoreFolders.Select(f => new Regex($@"\b{f}\b")).ToArray();

        // assuming you want to include nested folders
        var files = await Task.WhenAll(
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => !ignoreRegex.Any(rgx => rgx.IsMatch(f)))
                .OrderBy(p => p)
                .Select(async f => new { ContentBytes = await File.ReadAllBytesAsync(f), Path = f })
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

        return BitConverter.ToString(algorithm.Hash).Replace("-", "").ToLower();
    }

    public async Task<string> GetFolderHashAsync(string folder)
    {
        var hash = await GetFolderHashAsync(folder, new string[0], SHA256.Create());
        return hash;
    }

    public async Task<string> GetFolderHashAsync(string folder, IEnumerable<string> ignoreFolders)
    {
        var hash = await GetFolderHashAsync(folder, ignoreFolders, SHA256.Create());
        return hash;
    }
}
