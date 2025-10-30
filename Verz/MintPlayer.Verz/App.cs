using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MintPlayer.StringExtensions;
using MintPlayer.ApiHash;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuGet.Packaging;
using System.Reflection;
using System.Xml.Linq;
using System.Diagnostics;

namespace MintPlayer.Verz;

internal class App : IApp, IHelper
{
    public App() { }

    public async Task Run(string[] args)
    {
        if (args.Length >= 2 && args[0].Equals("dotnet", StringComparison.OrdinalIgnoreCase) && args[1].Equals("next", StringComparison.OrdinalIgnoreCase))
        {
            await RunDotnetNext(args.Skip(2).ToArray());
            return;
        }

        await ShowUsage();
    }

    public Task ShowUsage()
    {
        var versionString = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .ToString();

        var versionLine = $"verz v{versionString}";

        Console.WriteLine($"""
            {versionLine}
            {"-".Repeat(versionLine.Length)}
            Usage:
              verz dotnet next --project <path-to-csproj> [--tfm <tfm>] [--nuget-config <path>]
            """);

        return Task.CompletedTask;
    }

    private static (string packageId, string assemblyName, string? tfm, int major) ReadProjectDetails(string csprojPath, string? tfmArg)
    {
        var doc = XDocument.Load(csprojPath);
        XNamespace msbuild = doc.Root!.Name.Namespace;
        var pg = doc.Root.Elements(msbuild + "PropertyGroup");
        var packageId = pg.Elements(msbuild + "PackageId").Select(e => e.Value).FirstOrDefault();
        var assemblyName = pg.Elements(msbuild + "AssemblyName").Select(e => e.Value).FirstOrDefault();
        var tfm = pg.Elements(msbuild + "TargetFramework").Select(e => e.Value).FirstOrDefault();
        var tfms = pg.Elements(msbuild + "TargetFrameworks").Select(e => e.Value).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(packageId)) packageId = Path.GetFileNameWithoutExtension(csprojPath);
        if (string.IsNullOrWhiteSpace(assemblyName)) assemblyName = packageId;

        string? selectedTfm = tfmArg;
        if (string.IsNullOrWhiteSpace(selectedTfm))
        {
            if (!string.IsNullOrWhiteSpace(tfm)) selectedTfm = tfm;
            else if (!string.IsNullOrWhiteSpace(tfms))
            {
                selectedTfm = tfms.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .OrderByDescending(ParseTfmMajorMinor)
                    .FirstOrDefault();
            }
        }

        if (string.IsNullOrWhiteSpace(selectedTfm)) throw new InvalidOperationException("Unable to resolve TargetFramework");
        var major = ParseTfmMajorMinor(selectedTfm).major;
        return (packageId!, assemblyName!, selectedTfm, major);
    }

    private static (int major, int minor) ParseTfmMajorMinor(string tfm)
    {
        if (tfm.StartsWith("netstandard"))
        {
            var v = tfm.Substring("netstandard".Length);
            if (Version.TryParse(v, out var stdV)) return (stdV.Major, stdV.Minor);
            return (2, 0);
        }
        if (tfm.StartsWith("net"))
        {
            var v = tfm.Substring(3);
            if (Version.TryParse(v, out var netV)) return (netV.Major, netV.Minor);
        }
        return (0, 0);
    }

    private static async Task BuildProject(string csprojPath, string configuration)
    {
        var psi = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" -c {configuration} --nologo")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(csprojPath)!
        };
        var p = Process.Start(psi)!;
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
        {
            var err = await p.StandardError.ReadToEndAsync();
            throw new Exception($"Build failed for {csprojPath}: {err}");
        }
    }

    private static string GetBuiltAssemblyPath(string csprojPath, string configuration, string tfm, string assemblyName)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var dllPath = Path.Combine(projectDir, "bin", configuration, tfm, assemblyName + ".dll");
        if (!File.Exists(dllPath)) throw new FileNotFoundException("Built assembly not found", dllPath);
        return dllPath;
    }

    private static IEnumerable<PackageSource> GetPackageSources(string? nugetConfigPath)
    {
        ISettings settings;
        if (!string.IsNullOrWhiteSpace(nugetConfigPath))
        {
            var full = Path.GetFullPath(nugetConfigPath);
            settings = Settings.LoadSpecificSettings(Path.GetDirectoryName(full)!, Path.GetFileName(full));
        }
        else
        {
            settings = Settings.LoadDefaultSettings(Directory.GetCurrentDirectory());
        }
        var provider = new PackageSourceProvider(settings);
        return provider.LoadPackageSources().Where(s => s.IsEnabled);
    }

    private static async Task<IReadOnlyList<NuGetVersion>> GetAllPackageVersions(string packageId, IEnumerable<PackageSource> sources)
    {
        var cache = new SourceCacheContext();
        var versions = new HashSet<NuGetVersion>();
        foreach (var source in sources)
        {
            var repo = Repository.Factory.GetCoreV3(source);
            var finder = await repo.GetResourceAsync<FindPackageByIdResource>();
            var v = await finder.GetAllVersionsAsync(packageId, cache, NuGet.Common.NullLogger.Instance, CancellationToken.None);
            foreach (var nv in v) versions.Add(nv);
        }
        return versions.OrderBy(v => v).ToArray();
    }

    private static async Task<string?> TryDownloadPackageAndComputeApiHash(string packageId, NuGetVersion version, IEnumerable<PackageSource> sources, string tfm, string? preferredAssemblyName)
    {
        var cache = new SourceCacheContext();
        foreach (var source in sources)
        {
            try
            {
                var repo = Repository.Factory.GetCoreV3(source);
                var finder = await repo.GetResourceAsync<FindPackageByIdResource>();
                using var mem = new MemoryStream();
                var ok = await finder.CopyNupkgToStreamAsync(packageId, version, mem, cache, NuGet.Common.NullLogger.Instance, CancellationToken.None);
                if (!ok) continue;
                mem.Position = 0;
                using var reader = new PackageArchiveReader(mem);
                var refItems = reader.GetFiles($"ref/{tfm}");
                var libItems = reader.GetFiles($"lib/{tfm}");
                var dlls = refItems.Concat(libItems).Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (dlls.Length == 0) continue;
                string? pick = null;
                if (!string.IsNullOrWhiteSpace(preferredAssemblyName))
                {
                    pick = dlls.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Equals(preferredAssemblyName, StringComparison.OrdinalIgnoreCase));
                }
                pick ??= dlls.First();
                using var s = reader.GetStream(pick);
                var tempDir = Path.Combine(Path.GetTempPath(), "verz", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                var tempDll = Path.Combine(tempDir, Path.GetFileName(pick));
                using (var fs = File.Create(tempDll))
                {
                    await s.CopyToAsync(fs);
                }
                try
                {
                    return ApiHasher.ComputeHashFromAssembly(tempDll);
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch
            {
                // try next source
            }
        }
        return null;
    }

    private async Task RunDotnetNext(string[] args)
    {
        string? csproj = null;
        string? tfm = null;
        string? nugetConfig = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project":
                    csproj = args[++i];
                    break;
                case "--tfm":
                    tfm = args[++i];
                    break;
                case "--nuget-config":
                    nugetConfig = args[++i];
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(csproj) || !File.Exists(csproj)) throw new FileNotFoundException("Project file not found", csproj);

        var (packageId, assemblyName, resolvedTfm, tfmMajor) = ReadProjectDetails(csproj, tfm);

        await BuildProject(csproj, "Release");
        var assemblyPath = GetBuiltAssemblyPath(csproj, "Release", resolvedTfm!, assemblyName);
        var currentApiHash = ApiHasher.ComputeHashFromAssembly(assemblyPath);

        var sources = GetPackageSources(nugetConfig).ToArray();
        var versions = await GetAllPackageVersions(packageId, sources);
        var bandVersions = versions.Where(v => v.Major == tfmMajor && string.IsNullOrEmpty(v.Release)).ToArray();
        if (bandVersions.Length == 0)
        {
            Console.WriteLine($"{tfmMajor}.0.0");
            return;
        }

        var latest = bandVersions.Max();
        var previousHash = await TryDownloadPackageAndComputeApiHash(packageId, latest, sources, resolvedTfm!, assemblyName);
        if (previousHash == null)
        {
            Console.WriteLine($"{latest.Major}.{latest.Minor}.{latest.Patch + 1}");
            return;
        }

        if (string.Equals(previousHash, currentApiHash, StringComparison.Ordinal))
        {
            Console.WriteLine($"{latest.Major}.{latest.Minor}.{latest.Patch + 1}");
        }
        else
        {
            Console.WriteLine($"{latest.Major}.{latest.Minor + 1}.0");
        }
    }
}
