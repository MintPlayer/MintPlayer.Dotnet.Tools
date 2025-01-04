namespace MintPlayer.Verz.Abstractions;

public interface IDevelopmentSdk
{
    Task<string> GetPackageById(string packageId);
}
