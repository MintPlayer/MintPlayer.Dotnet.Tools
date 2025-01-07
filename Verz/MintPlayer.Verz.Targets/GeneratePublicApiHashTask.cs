using Microsoft.Build.Framework;
using PublicApiGenerator;
using System.Reflection;
using System.Security.Cryptography;
using RequiredAttribute = Microsoft.Build.Framework.RequiredAttribute;

public class GeneratePublicApiHashTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string AssemblyPath { get; set; }

    [Output]
    public string PublicApiHash { get; private set; }

    public override bool Execute()
    {
        try
        {
            if (!File.Exists(AssemblyPath))
            {
                Log.LogError($"Assembly not found at path: {AssemblyPath}");
                return false;
            }

            var assembly = Assembly.Load(AssemblyPath);
            var publicApi = ApiGenerator.GeneratePublicApi(assembly);

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(publicApi));
            PublicApiHash = Convert.ToHexString(hashBytes);

            Log.LogMessage(MessageImportance.High, $"Generated SHA256 hash: {PublicApiHash}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }
}
