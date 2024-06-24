namespace MintPlayer.FolderHasher.Abstractions;

public interface IFolderHasher
{
    Task<string> GetFolderHashAsync(string folder);
}
