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

    private static readonly string RepoRoot = Path.GetFullPath(
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

        var log = new List<string>();
        foreach (var project in ProjectsToPack)
        {
            var (exitCode, output) = Run(RepoRoot, $"pack \"{project}\" -c Release -o \"{Feed}\" -p:Version={Version} -tl:off");
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
    /// MintPlayer.SourceGenerators.Tools is in the list for a non-obvious reason: the generator
    /// itself references it with <c>PrivateAssets="all"</c> and ships it inside the analyzer
    /// folder, so it is NOT a direct dependency — but
    /// MintPlayer.ValueComparers.NewtonsoftJson references it normally, which makes it a
    /// transitive one. That is exactly the kind of thing an in-process test cannot see.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> ProjectsToPack =>
    [
        Path.Combine(RepoRoot, "SourceGenerators", "MintPlayer.SourceGenerators.Tools", "MintPlayer.SourceGenerators.Tools.csproj"),
        Path.Combine(RepoRoot, "SourceGenerators", "SourceGenerators", "MintPlayer.SourceGenerators.Attributes", "MintPlayer.SourceGenerators.Attributes.csproj"),
        Path.Combine(RepoRoot, "SourceGenerators", "ValueComparerGenerator", "MintPlayer.ValueComparerGenerator.Attributes", "MintPlayer.ValueComparerGenerator.Attributes.csproj"),
        Path.Combine(RepoRoot, "SourceGenerators", "ValueComparers", "MintPlayer.ValueComparers.NewtonsoftJson", "MintPlayer.ValueComparers.NewtonsoftJson.csproj"),
        Path.Combine(RepoRoot, "SourceGenerators", "SourceGenerators", "MintPlayer.SourceGenerators", "MintPlayer.SourceGenerators.csproj"),
        Path.Combine(RepoRoot, "Assertions", "MintPlayer.Assertions", "MintPlayer.Assertions.csproj"),
    ];

    /// <summary>
    /// Packs one project in Debug into a separate feed and returns its analyzer entries.
    /// </summary>
    /// <remarks>
    /// Configuration-conditional pack items are the trap this exists for: a
    /// <c>Condition="'$(Configuration)' == 'Release'"</c> on a <c>None Include</c> produces a
    /// package that is correct in CI and quietly broken from a developer's plain
    /// <c>dotnet pack</c>, which defaults to Debug. Nothing else in the suite would notice.
    ///
    /// Lazy and cached — most tests do not need it, and a second pack is not free.
    /// </remarks>
    public IReadOnlyList<string> DebugAnalyzerEntriesOf(string packageId, string projectRelativePath)
    {
        if (_debugEntries.TryGetValue(packageId, out var cached)) return cached;

        var debugFeed = Path.Combine(Root, "feed-debug");
        Directory.CreateDirectory(debugFeed);

        var project = Path.Combine(RepoRoot, projectRelativePath);
        var (exitCode, output) = Run(RepoRoot, $"pack \"{project}\" -c Debug -o \"{debugFeed}\" -p:Version={Version} -tl:off");

        if (exitCode != 0)
            throw new InvalidOperationException($"Debug pack of '{project}' failed.{Environment.NewLine}{output}");

        var entries = ReadEntries(Path.Combine(debugFeed, $"{packageId}.{Version}.nupkg"))
            .Where(e => e.StartsWith("analyzers/", StringComparison.Ordinal))
            .ToList();

        return _debugEntries[packageId] = entries;
    }

    private readonly Dictionary<string, IReadOnlyList<string>> _debugEntries = new(StringComparer.Ordinal);

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
    /// Unlike TokenReplacer's equivalent this does NOT clear the remote source. The generator
    /// package chain reaches MintPlayer.ValueComparers.NewtonsoftJson, which depends on
    /// Newtonsoft.Json — a third-party package the test cannot produce. In practice it is already
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
