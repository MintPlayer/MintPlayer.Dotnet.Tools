using System.Xml.Linq;
using MintPlayer.Verz.Core;
using PublicApiGenerator;
using System.Reflection;

namespace MintPlayer.Verz.Sdks.Dotnet;

public class DotnetSdk : IDevelopmentSdk
{
    public bool IsApplicable(string rootPath)
    {
        // Consider applicable if any .csproj exists
        return Directory.EnumerateFiles(rootPath, "*.csproj", SearchOption.AllDirectories).Any();
    }

    public async Task<IReadOnlyList<PackageInfo>> DiscoverPackagesAsync(string rootPath, CancellationToken cancellationToken)
    {
        var projects = Directory.EnumerateFiles(rootPath, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToList();

        var results = new List<PackageInfo>();
        foreach (var proj in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsPackable(proj)) continue;

            var (id, tfm) = ReadPackageIdAndTfm(proj);
            if (id == null || tfm == null) continue;

            if (!TryParseNetMajor(tfm, out var major)) continue;

            results.Add(new PackageInfo(proj, id!, tfm!, major));
        }

        return await Task.FromResult(results);
    }

    public async Task<string> ComputePublicApiHashAsync(PackageInfo package, CancellationToken cancellationToken)
    {
        // Try to locate built DLL (Release or Debug)
        var projectDir = Path.GetDirectoryName(package.ProjectPath)!;
        var dllName = GetAssemblyName(package.ProjectPath) + ".dll";

        // Prefer Release
        var candidates = new[]
        {
            Path.Combine(projectDir, "bin", "Release", package.TargetFramework, dllName),
            Path.Combine(projectDir, "bin", "Debug", package.TargetFramework, dllName)
        };

        var assemblyPath = candidates.FirstOrDefault(File.Exists);
        if (assemblyPath == null)
            throw new FileNotFoundException($"Built assembly not found for {package.ProjectPath}. Build the project first.");

        var asm = Assembly.LoadFrom(assemblyPath);
        var publicApi = ApiGenerator.GeneratePublicApi(asm);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(publicApi));
        var hash = Convert.ToHexString(hashBytes);
        return await Task.FromResult(hash);
    }

    private static bool IsPackable(string csprojPath)
    {
        try
        {
            var x = XDocument.Load(csprojPath);
            var ns = x.Root?.Name.Namespace ?? XNamespace.None;
            var isPackableStr = x.Descendants(ns + "IsPackable").FirstOrDefault()?.Value?.Trim();
            if (bool.TryParse(isPackableStr, out var isPackable)) return isPackable;

            // Default heuristic: class library projects are packable; SDK-style exe typically not
            var sdk = x.Root?.Attribute("Sdk")?.Value ?? "";
            return true;
        }
        catch { return false; }
    }

    private static (string? packageId, string? targetFramework) ReadPackageIdAndTfm(string csprojPath)
    {
        var x = XDocument.Load(csprojPath);
        var ns = x.Root?.Name.Namespace ?? XNamespace.None;
        var packageId = x.Descendants(ns + "PackageId").FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(packageId))
            packageId = x.Descendants(ns + "AssemblyName").FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(packageId))
            packageId = Path.GetFileNameWithoutExtension(csprojPath);

        // Choose highest netX TFM
        var tfm = x.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(tfm))
        {
            var tfms = x.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
            tfm = tfms
                .Where(TryParseNetMajor)
                .OrderByDescending(t => GetNetMajor(t))
                .FirstOrDefault();
        }

        return (packageId, tfm);
    }

    private static string GetAssemblyName(string csprojPath)
    {
        var x = XDocument.Load(csprojPath);
        var ns = x.Root?.Name.Namespace ?? XNamespace.None;
        var value = x.Descendants(ns + "AssemblyName").FirstOrDefault()?.Value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? Path.GetFileNameWithoutExtension(csprojPath) : value;
    }

    private static bool TryParseNetMajor(string? tfm, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(tfm)) return false;
        if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase)) return false;
        var ver = tfm[3..]; // e.g., 8.0, 9.0, 48 for net48
        if (ver.Contains('.'))
        {
            if (int.TryParse(ver.Split('.')[0], out major)) return true;
        }
        else if (ver.Length >= 2 && int.TryParse(ver.Substring(0, 2), out var legacy))
        {
            // net48 -> treat as 4
            major = legacy / 10;
            return true;
        }
        return false;
    }

    private static bool TryParseNetMajor(string tfm) => TryParseNetMajor(tfm, out _);
    private static int GetNetMajor(string tfm)
    {
        TryParseNetMajor(tfm, out var m);
        return m;
    }
}
