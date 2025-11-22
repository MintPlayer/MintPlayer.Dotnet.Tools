using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using System.Diagnostics;

namespace MintPlayer.LocalPackagePublisher;

public interface IPackagePublisher
{
    Task RunAsync(string sourceName, string version, bool dryRun);
}

[Register(typeof(IPackagePublisher), ServiceLifetime.Transient)]
internal partial class PackagePublisher : IPackagePublisher
{
    [Inject] private readonly INugetConfigResolver nugetConfigResolver;

    public async Task RunAsync(string sourceName, string version, bool dryRun)
    {
        var cwd = Directory.GetCurrentDirectory();
        Console.WriteLine($"Current directory: {cwd}");
        Console.WriteLine($"Target version:    {version}");
        Console.WriteLine($"Source name:       {sourceName}");
        Console.WriteLine();

        // 1️⃣ Resolve source name → path
        var nugetSources = nugetConfigResolver.LoadPackageSources(cwd);
        if (!nugetSources.TryGetValue(sourceName, out var rawSourceValue))
            throw new InvalidOperationException($"Could not find a package source named '{sourceName}' in nuget.config hierarchy.");

        var sourcePath = rawSourceValue;
        if (Uri.TryCreate(rawSourceValue, UriKind.Absolute, out var uri) &&
            uri.Scheme != Uri.UriSchemeFile &&
            !string.IsNullOrWhiteSpace(uri.Scheme))
        {
            throw new InvalidOperationException($"Package source '{sourceName}' points to a non-file URI '{rawSourceValue}'. Only local sources are supported.");
        }

        if (!Path.IsPathRooted(sourcePath))
            sourcePath = Path.GetFullPath(Path.Combine(cwd, sourcePath));

        Console.WriteLine($"Resolved local source folder: {sourcePath}");
        Console.WriteLine();

        if (dryRun)
            Console.WriteLine("Dry run: no changes will be made.\n");

        if (!dryRun && !Directory.Exists(sourcePath))
        {
            Console.WriteLine($"Creating source directory '{sourcePath}'...");
            Directory.CreateDirectory(sourcePath);
        }

        // 2️⃣ Enumerate projects
        var projects = FindProjects(cwd).ToList();
        if (projects.Count == 0)
        {
            Console.WriteLine("No .csproj files found under the current directory.");
            return;
        }

        Console.WriteLine("Projects found:");
        foreach (var p in projects)
            Console.WriteLine($"  - {p}");
        Console.WriteLine();

        // 3️⃣ Pack & copy each project
        foreach (var projectPath in projects)
        {
            var projectDir = Path.GetDirectoryName(projectPath)!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var outputDir = Path.Combine(projectDir, ".artifacts", "pack", projectName);

            Console.WriteLine($"=== {projectName} ===");
            Console.WriteLine($"Project:  {projectPath}");
            Console.WriteLine($"Pack out: {outputDir}");

            if (!dryRun)
                Directory.CreateDirectory(outputDir);

            var packArgs = new[]
            {
                "pack",
                $"\"{projectPath}\"",
                "-c", "Release",
                "-nologo",
                "-v", "minimal",
                "-o", $"\"{outputDir}\"",
                $"/p:PackageVersion={version}"
            };

            if (dryRun)
            {
                Console.WriteLine("[dry-run] dotnet " + string.Join(" ", packArgs));
            }
            else
            {
                var exitCode = await RunProcessAsync("dotnet", string.Join(" ", packArgs));
                if (exitCode != 0)
                {
                    Console.WriteLine($"dotnet pack failed for {projectName} (exit {exitCode})\n");
                    continue;
                }
            }

            var nupkgs = Directory.GetFiles(outputDir, "*.nupkg", SearchOption.TopDirectoryOnly);
            if (nupkgs.Length == 0)
            {
                Console.WriteLine("No .nupkg files produced.\n");
                continue;
            }

            foreach (var nupkg in nupkgs)
            {
                var dest = Path.Combine(sourcePath, Path.GetFileName(nupkg));
                if (dryRun)
                    Console.WriteLine($"[dry-run] copy \"{nupkg}\" → \"{dest}\"");
                else
                {
                    File.Copy(nupkg, dest, overwrite: true);
                    Console.WriteLine($"Copied to {dest}");
                }
            }

            Console.WriteLine();
        }

        Console.WriteLine("✅ Done.");
    }

    private static IEnumerable<string> FindProjects(string root)
    {
        var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "bin", "obj", "node_modules", ".vs", ".idea", ".artifacts"
        };

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string>? subDirs = null;
            try { subDirs = Directory.GetDirectories(current); } catch { }

            if (subDirs != null)
            {
                foreach (var dir in subDirs)
                {
                    var name = Path.GetFileName(dir);
                    if (!skipDirs.Contains(name))
                        stack.Push(dir);
                }
            }

            IEnumerable<string>? files = null;
            try { files = Directory.GetFiles(current, "*.csproj", SearchOption.TopDirectoryOnly); } catch { }

            if (files != null)
            {
                foreach (var file in files)
                    yield return file;
            }
        }
    }

    private static async Task<int> RunProcessAsync(string fileName, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.Error.WriteLine(e.Data);
                Console.ResetColor();
            }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();
        return proc.ExitCode;
    }
}