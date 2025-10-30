using System.Security.Cryptography;
using System.Text;
using PublicApiGenerator;
using System.Reflection;

namespace MintPlayer.ApiHash;

public static class ApiHasher
{
    public static string ComputeHashFromAssembly(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var apiText = ApiGenerator.GeneratePublicApi(assembly);
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(apiText);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
