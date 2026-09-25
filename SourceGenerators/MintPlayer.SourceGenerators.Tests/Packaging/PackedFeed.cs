using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace MintPlayer.SourceGenerators.Tests.Packaging;

/// <summary>
/// Packs the real generator packages into a temp local feed once, for the whole test class.
/// </summary>
/// <remarks>
/// <para>
/// Everything else in this test project runs generators in-process, which deliberately bypasses
/// MSBuild and NuGet entirely. That is the right shape for testing generator LOGIC, and it is
/// blind by construction to the thing that actually reaches consumers: the package. A generator
/// can be flawless and still ship broken if its DLL lands in the wrong folder, its runtime
/// dependency is left out, or its props/targets do not wire it in.
/// </para>
/// <para>
/// Packing is slow — around a minute for the five projects — so it happens once per class through
/// an <c>IClassFixture</c> rather than per test.
/// </para>
/// </remarks>
public sealed class PackedFeed : IDisposable
{
    /// <summary>Distinctive so a stale real package can never satisfy a restore by accident.</summary>
    public const string Version = "99.9.9-packtest";

    internal static readonly string RepoRoot = Path.GetFullPath(
        typeof(PackedFeed).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!);

    public string Root { get; }
    public string Feed { get; }

    /// <summary>Output of every pack, for a failure message that says what actually happened.</summary>
    public string PackLog { get; } = string.Empty;

    public PackedFeed()
    {
        Root = Path.Combine(Path.GetTempPath(), "mpsg-pack", Guid.NewGuid().ToString("N"));
        Feed = Path.Combine(Root, "feed");
        Directory.CreateDirectory(Feed);

        var tree = CopyTree(Path.Combine(Root, "src"));
        var log = new List<string>();
        foreach (var project in ProjectsToPack.Select(p => Path.Combine(tree, p)))
        {
            var (exitCode, output) = Run(tree, $"pack \"{project}\" -c Release -o \"{Feed}\" -p:Version={Version} -tl:off");
            log.Add($"--- pack {Path.GetFileName(project)} (exit {exitCode}) ---{Environment.NewLine}{output}");

            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"Packing '{project}' failed. Every packaging assertion depends on this, so it is a hard " +
                    $"failure rather than a skipped test.{Environment.NewLine}{output}");
        }

        PackLog = string.Join(Environment.NewLine, log);
    }

    /// <summary>
    /// The generator package plus everything its nuspec declares a dependency on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything in the generator package's dependency closure, because <c>-p:Version</c> stamps
    /// every one of them with the test version — so a real package on nuget.org can never satisfy
    /// the restore, and any omission fails with NU1102 rather than anything pointing at the cause.
    /// </para>
    /// <para>
    /// MintPlayer.SourceGenerators.Tools and MintPlayer.ValueComparers.NewtonsoftJson are in the list
    /// although, since 12.0.0, neither is in the closure: the generator references Tools with
    /// <c>PrivateAssets="all"</c> and ships it inside the analyzer folder, and it no longer imports the
    /// Newtonsoft comparer (which used to make Tools a transitive dependency). Packing them anyway is
    /// cheap, and it keeps the feed able to satisfy a dependency that comes back — which would otherwise
    /// fail with NU1102, the kind of thing an in-process test cannot see.
    /// </para>
    /// </remarks>
    /// <summary>Relative to the repository root (and to each copy of it).</summary>
    internal static IEnumerable<string> ProjectsToPack =>
    [
        Path.Combine("SourceGenerators", "MintPlayer.SourceGenerators.Tools", "MintPlayer.SourceGenerators.Tools.csproj"),
        Path.Combine("SourceGenerators", "SourceGenerators", "MintPlayer.SourceGenerators.Attributes", "MintPlayer.SourceGenerators.Attributes.csproj"),
        Path.Combine("SourceGenerators", "ValueComparerGenerator", "MintPlayer.ValueComparerGenerator.Attributes", "MintPlayer.ValueComparerGenerator.Attributes.csproj"),
        Path.Combine("SourceGenerators", "ValueComparers", "MintPlayer.ValueComparers.NewtonsoftJson", "MintPlayer.ValueComparers.NewtonsoftJson.csproj"),
        Path.Combine("SourceGenerators", "SourceGenerators", "MintPlayer.SourceGenerators", "MintPlayer.SourceGenerators.csproj"),
        Path.Combine("Assertions", "MintPlayer.Assertions", "MintPlayer.Assertions.csproj"),
    ];

    /// <summary>
    /// Copies the source the packs need into <paramref name="destination"/>, without any bin/ or obj/.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Packing in the repo itself rebuilt every project's bin/Release with the 99.9.9 test stamp, and
    /// CI's later <c>dotnet pack --no-build</c> shipped those dlls. Every release from 10.20.2 to 12.0.1
    /// went out as AssemblyVersion 99.9.9.0 that way.
    /// </para>
    /// <para>
    /// A copy, not <c>-p:ArtifactsPath</c> or <c>-p:BaseOutputPath</c>. The pack targets
    /// (eng/sourcegenerator.targets and the others) read their payload from the hardcoded
    /// <c>$(MSBuildProjectDirectory)\bin\$(Configuration)\netstandard2.0</c>. Redirecting the output
    /// leaves that path holding the repo's real build, which then gets packed with no error. Measured:
    /// it produced a 99.9.9-packtest package full of 12.0.1.0 dlls. A copy with no bin/ or obj/ also
    /// gives a clean tree, so no leftover build state leaks into what gets packed. The copy takes
    /// about a second.
    /// </para>
    /// </remarks>
    private static string CopyTree(string destination)
    {
        foreach (var dir in new[] { "SourceGenerators", "Assertions" })
            CopyDirectory(Path.Combine(RepoRoot, dir), Path.Combine(destination, dir));
        File.Copy(Path.Combine(RepoRoot, "nuget.config"), Path.Combine(destination, "nuget.config"));
        return destination;
    }

    private static readonly HashSet<string> SkippedDirectories =
        new(["bin", "obj", "TestResults", "node_modules", ".vs"], StringComparer.OrdinalIgnoreCase);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(dir))) continue;
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }


    /// <summary>
    /// Packs one project on its own, in the given configuration, and returns its analyzer entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOTH sides of the Debug-vs-Release comparison go through here rather than one side reusing
    /// the shared feed. The shared feed is packed after the test run's own <c>dotnet build</c> has
    /// populated every <c>bin/Release</c> in the solution, while an isolated pack builds only what
    /// that one project references. Comparing those two measures leftover build state, not the
    /// packaging logic, and reports a difference that is not a defect — which is exactly what it
    /// did on the first attempt.
    /// </para>
    /// <para>
    /// Cached per package and configuration; each pack costs roughly ten seconds.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> IsolatedAnalyzerEntriesOf(string packageId, string projectRelativePath, string configuration)
    {
        var key = $"{packageId}|{configuration}";
        if (_isolatedEntries.TryGetValue(key, out var cached)) return cached;

        var feedDir = Path.Combine(Root, $"feed-{configuration.ToLowerInvariant()}");
        Directory.CreateDirectory(feedDir);

        // Forward slashes in, native separators out. Path.Combine does NOT translate separators,
        // so a backslash-separated literal resolves on Windows and silently does not on Linux.
        // Its own copy, so this pack cannot pick up what the shared feed's packs built.
        var tree = CopyTree(Path.Combine(Root, $"src-{packageId}-{configuration.ToLowerInvariant()}"));
        var project = Path.Combine(tree, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var (exitCode, output) = Run(tree, $"pack \"{project}\" -c {configuration} -o \"{feedDir}\" -p:Version={Version} -tl:off");

        if (exitCode != 0)
            throw new InvalidOperationException($"{configuration} pack of '{project}' failed.{Environment.NewLine}{output}");

        var entries = ReadEntries(Path.Combine(feedDir, $"{packageId}.{Version}.nupkg"))
            .Where(e => e.StartsWith("analyzers/", StringComparison.Ordinal))
            .ToList();

        return _isolatedEntries[key] = entries;
    }

    private readonly Dictionary<string, IReadOnlyList<string>> _isolatedEntries = new(StringComparer.Ordinal);

    public string NupkgPath(string packageId) => Path.Combine(Feed, $"{packageId}.{Version}.nupkg");

    /// <summary>Every entry in a packed nupkg, with forward slashes and NuGet's own plumbing removed.</summary>
    public IReadOnlyList<string> EntriesOf(string packageId)
    {
        var path = NupkgPath(packageId);
        File.Exists(path).Should().BeTrue($"'{packageId}' should have been packed into the feed at '{path}'");
        return ReadEntries(path);
    }

    private static IReadOnlyList<string> ReadEntries(string nupkgPath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        return archive.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(n => !n.StartsWith("_rels/", StringComparison.Ordinal)
                     && !n.StartsWith("package/", StringComparison.Ordinal)
                     && !n.EndsWith(".psmdcp", StringComparison.Ordinal)
                     && n != "[Content_Types].xml")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A nuget.config pointing at the local feed, with nuget.org kept as a fallback.
    /// </summary>
    /// <remarks>
    /// Unlike TokenReplacer's equivalent this does NOT clear the remote source. The packed
    /// MintPlayer.ValueComparers.NewtonsoftJson depends on Newtonsoft.Json, and the consumer
    /// fixtures restore third-party packages the test cannot produce. In practice they are already
    /// in the machine's global packages folder (the repo itself references it), so restore is
    /// usually offline anyway; nuget.org is there so a cold machine fails slowly rather than
    /// confusingly.
    ///
    /// The global packages folder is deliberately NOT redirected: an isolated one would force a
    /// genuine re-download of every transitive dependency on every run.
    /// </remarks>
    public string NuGetConfigXml => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
        	<packageSources>
        		<clear />
        		<add key="packtest-local" value="{Feed.Replace('\\', '/')}" />
        		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
        	</packageSources>
        	<config>
        		<!--
        		Its own packages folder: the global one keeps whatever 99.9.9-packtest package an earlier
        		run restored, and a restore takes that copy over the one just packed.
        		-->
        		<add key="globalPackagesFolder" value="{Path.Combine(Root, "packages").Replace('\\', '/')}" />
        	</config>
        </configuration>
        """;

    public static (int ExitCode, string Output) Run(string workingDirectory, string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(5 * 60 * 1000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {arguments} did not finish within 5 minutes.");
        }

        return (process.ExitCode, stdout.Result + Environment.NewLine + stderr.Result);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* best effort; temp dir */ }
        catch (UnauthorizedAccessException) { /* best effort; temp dir */ }
    }
}
