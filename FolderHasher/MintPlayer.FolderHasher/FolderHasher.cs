using MintPlayer.FolderHasher.Abstractions;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MintPlayer.FolderHasher;

internal class FolderHasher : IFolderHasher
{
    public async Task<string> GetFolderHashAsync(string folder)
    {
        // assuming you want to include nested folders
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                             .OrderBy(p => p)
                             .ToList();

        var sha = SHA256.Create();

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];

            // hash path
            var relativePath = file.Substring(path.Length + 1);
            var pathBytes = Encoding.UTF8.GetBytes(relativePath.ToLower());
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);

            // hash contents
            var contentBytes = await File.ReadAllBytesAsync(file);
            if (i == files.Count - 1)
                sha.TransformFinalBlock(contentBytes, 0, contentBytes.Length);
            else
                sha.TransformBlock(contentBytes, 0, contentBytes.Length, contentBytes, 0);
        }

        return BitConverter.ToString(sha.Hash).Replace("-", "").ToLower();
    }
}
