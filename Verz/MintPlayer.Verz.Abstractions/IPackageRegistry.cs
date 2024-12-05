namespace MintPlayer.Verz.Abstractions;

public interface IPackageRegistry
{
    Task<IEnumerable<string>> GetPackageVersions(string packageId);
}
