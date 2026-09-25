using System.Text.RegularExpressions;

namespace MintPlayer.SourceGenerators.Tests.Guards;

/// <summary>
/// Acceptance criterion 3 of PRD-GeneratedEquality: the value-comparer runtime is gone and stays gone.
/// </summary>
/// <remarks>
/// <para>
/// 12.0.0 replaced <c>ValueComparer&lt;T&gt;</c>, <c>ComparerRegistry</c>, <c>[ValueComparer]</c>,
/// <c>ICompilationCache</c> and the <c>.WithComparer()</c> pipeline operators with equality generated on the
/// model itself. The types are deleted, so production code that names them no longer compiles; this test also
/// catches what the compiler cannot: a comment, a commented-out call, or a fixture string that still teaches the
/// old API. The same release renamed <c>[AutoValueComparer]</c> to <c>[GenerateEquality]</c> and
/// <c>[ComparerIgnore]</c> to <c>[EqualityIgnore]</c>, without aliases, so the old attribute names are forbidden too.
/// </para>
/// <para>
/// Test projects (<c>*.Tests</c>) are excluded: they legitimately name the removed APIs in assertions that the
/// generated code does NOT contain them, and in the explanation of what replaced them.
/// </para>
/// </remarks>
public class RemovedComparerApiGuardTests
{
    private static readonly (string Name, Regex Pattern)[] Forbidden =
    [
        (".WithComparer(", new Regex(@"\.WithComparer\(", RegexOptions.Compiled)),
        ("WithNullableComparer", new Regex(@"WithNullableComparer", RegexOptions.Compiled)),
        ("ComparerRegistry", new Regex(@"ComparerRegistry", RegexOptions.Compiled)),
        ("ValueComparer<", new Regex(@"\bValueComparer<", RegexOptions.Compiled)),
        ("ICompilationCache", new Regex(@"ICompilationCache", RegexOptions.Compiled)),
        ("[ValueComparer(", new Regex(@"\[ValueComparer\(", RegexOptions.Compiled)),
        ("[AutoValueComparer", new Regex(@"\[AutoValueComparer\b", RegexOptions.Compiled)),
        ("[ComparerIgnore", new Regex(@"\[ComparerIgnore\b", RegexOptions.Compiled)),
    ];

    /// <summary>The source roots that ship code built on Tools.</summary>
    private static readonly string[] ScannedRoots = ["SourceGenerators", "Assertions"];

    [Fact]
    public void NoSourceFileUsesTheRemovedComparerApis()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in ScannedRoots
            .Select(r => Path.Combine(root, r))
            .Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories))
            .Where(f => !IsExcluded(root, f)))
        {
            scanned++;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (var (name, pattern) in Forbidden)
                    if (pattern.IsMatch(lines[i]))
                        offenders.Add($"{Path.GetRelativePath(root, file)}({i + 1}): {name}");
        }

        scanned.Should().BeGreaterThan(100, "the scan must actually reach the generator sources");
        offenders.Should().BeEmpty(string.Join(Environment.NewLine, offenders));
    }

    /// <summary>bin/obj output, and every test project.</summary>
    private static bool IsExcluded(string root, string file)
    {
        var segments = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => s is "bin" or "obj" || s.EndsWith(".Tests", StringComparison.Ordinal));
    }

    /// <summary>Walks up from the test binaries to the directory holding the solution file.</summary>
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MintPlayer.Dotnet.Tools.sln")))
                return dir.FullName;

        throw new InvalidOperationException($"MintPlayer.Dotnet.Tools.sln was not found above '{AppContext.BaseDirectory}'.");
    }
}
