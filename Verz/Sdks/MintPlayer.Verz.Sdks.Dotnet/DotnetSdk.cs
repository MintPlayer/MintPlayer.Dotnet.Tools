using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MintPlayer.Verz.Core;
using NuGet.Packaging;
using PublicApiGenerator;

namespace MintPlayer.Verz.Sdks.Dotnet;

public class DotnetSdk : IDevelopmentSdk
{
    public bool CanHandle(string projectPath)
    {
        return projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> GetPackageIdAsync(string projectPath, CancellationToken cancellationToken)
    {
        var doc = XDocument.Load(projectPath);
        var ns = doc.Root?.Name.Namespace;
        var packageId = doc.Descendants(ns + "PackageId").FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(packageId))
        {
            packageId = Path.GetFileNameWithoutExtension(projectPath);
        }
        return Task.FromResult(packageId!);
    }

    public Task<int> GetMajorVersionAsync(string projectPath, CancellationToken cancellationToken)
    {
        var doc = XDocument.Load(projectPath);
        var ns = doc.Root?.Name.Namespace;
        var tfm = doc.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(tfm))
        {
            var tfms = doc.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value?.Trim();
            tfm = tfms?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Trim())
                .Where(IsNetTfm)
                .OrderByDescending(ParseNetMajor)
                .FirstOrDefault();
        }
        if (string.IsNullOrWhiteSpace(tfm) || !IsNetTfm(tfm))
            throw new InvalidOperationException($"Cannot determine .NET TargetFramework for '{projectPath}'.");

        var major = ParseNetMajor(tfm);
        return Task.FromResult(major);
    }

    public async Task<string> ComputeCurrentPublicApiHashAsync(string projectPath, string configuration, CancellationToken cancellationToken)
    {
        var doc = XDocument.Load(projectPath);
        var ns = doc.Root?.Name.Namespace;
        var assemblyName = doc.Descendants(ns + "AssemblyName").FirstOrDefault()?.Value?.Trim()
            ?? Path.GetFileNameWithoutExtension(projectPath);

        // Pick highest netX.Y TFM
        var tfms = new List<string>();
        var tfm = doc.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(tfm)) tfms.Add(tfm);
        var tfmsMulti = doc.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(tfmsMulti))
            tfms.AddRange(tfmsMulti.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var selectedTfm = tfms.Where(IsNetTfm).OrderByDescending(ParseNetMajor).ThenByDescending(ParseNetMinor).FirstOrDefault()
            ?? throw new InvalidOperationException("No supported net TargetFramework found.");

        var projectDir = Path.GetDirectoryName(projectPath)!;
        var outputPath = Path.Combine(projectDir, "bin", configuration, selectedTfm, assemblyName + ".dll");
        if (!File.Exists(outputPath))
        {
            throw new FileNotFoundException($"Build output not found. Build the project first: {outputPath}");
        }

        var assembly = System.Reflection.Assembly.LoadFrom(outputPath);
        var publicApi = ApiGenerator.GeneratePublicApi(assembly);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(publicApi));
        return Convert.ToHexString(hashBytes);
    }

    public Task<string?> ComputePackagePublicApiHashAsync(Stream nupkgStream, int majorVersion, CancellationToken cancellationToken)
    {
        using var reader = new PackageArchiveReader(nupkgStream, leaveStreamOpen: true);
        // Try read nuspec <PublicApiHash>
        try
        {
            using var nuspec = reader.GetNuspec();
            var xdoc = XDocument.Load(nuspec);
            var metadata = xdoc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase));
            var hashElem = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("PublicApiHash", StringComparison.OrdinalIgnoreCase));
            var value = hashElem?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return Task.FromResult<string?>(value);
        }
        catch
        {
            // ignore and fallback to compute from assembly
        }

        // Fallback: compute from contained lib assembly that matches major
        var files = reader.GetFiles().Where(f => f.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)).ToList();
        var candidate = files
            .Select(f => new { Path = f, Parts = f.Split('/') })
            .Where(x => x.Parts.Length >= 3 && x.Parts[1].StartsWith("net", StringComparison.OrdinalIgnoreCase))
            .Select(x => new { x.Path, Tfm = x.Parts[1], File = x.Parts[^1] })
            .Where(x => TryParseNetMajor(x.Tfm, out var maj) && maj == majorVersion && x.File.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => ParseNetMinor(x.Tfm))
            .FirstOrDefault();

        if (candidate == null)
            return Task.FromResult<string?>(null);

        using var dll = reader.GetStream(candidate.Path);
        using var temp = new MemoryStream();
        dll.CopyTo(temp);
        temp.Position = 0;
        var raw = temp.ToArray();
        var assembly = System.Reflection.Assembly.Load(raw);
        var publicApi = ApiGenerator.GeneratePublicApi(assembly);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(publicApi));
        return Task.FromResult<string?>(Convert.ToHexString(hashBytes));
    }

    private static bool IsNetTfm(string tfm) => tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) && tfm.Length >= 4 && char.IsDigit(tfm[3]);
    private static int ParseNetMajor(string tfm)
    {
        // net8.0 -> 8, net10.0 -> 10
        var digits = new string(tfm.Skip(3).TakeWhile(c => char.IsDigit(c)).ToArray());
        if (int.TryParse(digits, out var major)) return major;
        return 0;
    }
    private static int ParseNetMinor(string tfm)
    {
        var idx = tfm.IndexOf('.') + 1;
        if (idx > 0)
        {
            var minorDigits = new string(tfm.Skip(idx).TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(minorDigits, out var minor)) return minor;
        }
        return 0;
    }
    private static bool TryParseNetMajor(string tfm, out int major)
    {
        try
        {
            major = ParseNetMajor(tfm);
            return major > 0;
        }
        catch { major = 0; return false; }
    }
}
